import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AxiosError, type InternalAxiosRequestConfig } from 'axios'
import { Route, Routes } from 'react-router'
import { vi } from 'vitest'
import { useAuthStore } from '@/app/store/authStore'
import { makeUser, renderWithProviders } from '@/test/utils'

vi.mock('../api/authApi', () => ({
  authApi: { login: vi.fn(), me: vi.fn(), resendVerification: vi.fn() },
}))

import { authApi } from '../api/authApi'
import LoginPage from './LoginPage'

const tokens = { accessToken: 'jwt', tokenType: 'Bearer', expiresInSeconds: 900, expiresAt: new Date(Date.now() + 900_000).toISOString() }

function problem(status: number, code: string, detail: string) {
  const config = { headers: {} } as InternalAxiosRequestConfig
  return new AxiosError('failed', String(status), config, null, {
    status,
    statusText: '',
    headers: {},
    config,
    data: { status, code, detail, title: 'x' },
  })
}

function renderLogin(from?: string) {
  return renderWithProviders(
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/dashboard" element={<p>Dashboard page</p>} />
      <Route path="/settings/users" element={<p>Users page</p>} />
      <Route path="/platform/organizations" element={<p>Platform page</p>} />
      <Route path="/register" element={<p>Register page</p>} />
    </Routes>,
    { route: { pathname: '/login', state: from ? { from } : undefined } },
  )
}

async function fill(user: ReturnType<typeof userEvent.setup>, email: string, password: string) {
  await user.type(screen.getByLabelText(/email address/i), email)
  await user.type(screen.getByLabelText('Password'), password)
}

describe('LoginPage', () => {
  beforeEach(() => {
    vi.mocked(authApi.login).mockReset()
    vi.mocked(authApi.me).mockReset()
    vi.mocked(authApi.resendVerification).mockReset()
  })

  it('offers sign-in, password recovery and sign-up', () => {
    renderLogin()

    expect(screen.getByRole('heading', { name: /welcome back/i })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /forgot your password/i })).toHaveAttribute('href', '/forgot-password')
    expect(screen.getByRole('link', { name: /create your organization/i })).toHaveAttribute('href', '/register')
  })

  it('validates before calling the API', async () => {
    const user = userEvent.setup()
    renderLogin()

    await user.click(screen.getByRole('button', { name: /^sign in$/i }))

    expect(await screen.findByText('Enter your email address.')).toBeInTheDocument()
    expect(screen.getByText('Enter your password.')).toBeInTheDocument()
    expect(authApi.login).not.toHaveBeenCalled()
  })

  it('signs in, loads the profile and goes to the dashboard', async () => {
    const user = userEvent.setup()
    vi.mocked(authApi.login).mockResolvedValue(tokens)
    vi.mocked(authApi.me).mockResolvedValue(makeUser())
    renderLogin()

    await fill(user, 'ada@hope.org', 'Correct-Horse-Battery9')
    await user.click(screen.getByRole('button', { name: /^sign in$/i }))

    expect(await screen.findByText('Dashboard page')).toBeInTheDocument()
    expect(authApi.login).toHaveBeenCalledWith({ email: 'ada@hope.org', password: 'Correct-Horse-Battery9' })
    expect(useAuthStore.getState()).toMatchObject({ status: 'authenticated', accessToken: 'jwt' })
  })

  it('returns to the page the user was trying to reach', async () => {
    const user = userEvent.setup()
    vi.mocked(authApi.login).mockResolvedValue(tokens)
    vi.mocked(authApi.me).mockResolvedValue(makeUser())
    renderLogin('/settings/users')

    await fill(user, 'ada@hope.org', 'Correct-Horse-Battery9')
    await user.click(screen.getByRole('button', { name: /^sign in$/i }))

    expect(await screen.findByText('Users page')).toBeInTheDocument()
  })

  it('ignores a hostile return address', async () => {
    const user = userEvent.setup()
    vi.mocked(authApi.login).mockResolvedValue(tokens)
    vi.mocked(authApi.me).mockResolvedValue(makeUser())
    renderLogin('https://evil.example/phish')

    await fill(user, 'ada@hope.org', 'Correct-Horse-Battery9')
    await user.click(screen.getByRole('button', { name: /^sign in$/i }))

    expect(await screen.findByText('Dashboard page')).toBeInTheDocument()
  })

  it('sends platform operators to the organization registry', async () => {
    const user = userEvent.setup()
    vi.mocked(authApi.login).mockResolvedValue(tokens)
    vi.mocked(authApi.me).mockResolvedValue(makeUser({ isPlatformUser: true, organization: null, roles: ['SUPER_ADMIN'], permissions: ['Platform.Manage'] }))
    renderLogin()

    await fill(user, 'root@fundflow.test', 'Correct-Horse-Battery9')
    await user.click(screen.getByRole('button', { name: /^sign in$/i }))

    expect(await screen.findByText('Platform page')).toBeInTheDocument()
  })

  it('shows the server message for bad credentials and stays on the page', async () => {
    const user = userEvent.setup()
    vi.mocked(authApi.login).mockRejectedValue(problem(401, 'invalid_credentials', 'Invalid email or password.'))
    renderLogin()

    await fill(user, 'ada@hope.org', 'wrong-password')
    await user.click(screen.getByRole('button', { name: /^sign in$/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Invalid email or password.')
    expect(useAuthStore.getState().status).toBe('anonymous')
    expect(screen.queryByText('Dashboard page')).not.toBeInTheDocument()
  })

  it('offers to resend the verification email when sign-in is blocked by an unverified address', async () => {
    const user = userEvent.setup()
    vi.mocked(authApi.login).mockRejectedValue(problem(401, 'email_not_verified', 'Verify your email address before signing in.'))
    vi.mocked(authApi.resendVerification).mockResolvedValue(undefined)
    renderLogin()

    await fill(user, 'new@hope.org', 'Correct-Horse-Battery9')
    await user.click(screen.getByRole('button', { name: /^sign in$/i }))
    await user.click(await screen.findByRole('button', { name: /resend link/i }))

    await waitFor(() => expect(authApi.resendVerification).toHaveBeenCalledWith('new@hope.org'))
    expect(await screen.findByText(/new link is on its way/i)).toBeInTheDocument()
  })

  it('explains a lockout without offering a resend', async () => {
    const user = userEvent.setup()
    vi.mocked(authApi.login).mockRejectedValue(problem(401, 'account_locked', 'Too many failed sign-in attempts. Try again in 15 minutes.'))
    renderLogin()

    await fill(user, 'ada@hope.org', 'whatever')
    await user.click(screen.getByRole('button', { name: /^sign in$/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Try again in 15 minutes.')
    expect(screen.queryByRole('button', { name: /resend/i })).not.toBeInTheDocument()
  })

  it('can reveal and hide the typed password', async () => {
    const user = userEvent.setup()
    renderLogin()
    const field = screen.getByLabelText('Password')
    expect(field).toHaveAttribute('type', 'password')

    await user.click(screen.getByRole('button', { name: /show password/i }))
    expect(field).toHaveAttribute('type', 'text')
    await user.click(screen.getByRole('button', { name: /hide password/i }))
    expect(field).toHaveAttribute('type', 'password')
  })

  it('disables the button while signing in to prevent double submits', async () => {
    const user = userEvent.setup()
    let release!: () => void
    vi.mocked(authApi.login).mockReturnValue(new Promise((resolve) => { release = () => resolve(tokens) }))
    vi.mocked(authApi.me).mockResolvedValue(makeUser())
    renderLogin()

    await fill(user, 'ada@hope.org', 'Correct-Horse-Battery9')
    await user.click(screen.getByRole('button', { name: /^sign in$/i }))

    expect(await screen.findByRole('button', { name: /signing in/i })).toBeDisabled()
    release()
    expect(await screen.findByText('Dashboard page')).toBeInTheDocument()
    expect(authApi.login).toHaveBeenCalledTimes(1)
  })
})
