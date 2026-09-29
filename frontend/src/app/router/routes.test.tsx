import { ThemeProvider } from '@mui/material/styles'
import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { createMemoryRouter, matchRoutes, RouterProvider } from 'react-router'
import { vi } from 'vitest'
import { createAppTheme } from '@/app/theme/theme'
import { paths } from '@/shared/constants/paths'
import { createTestQueryClient, makeUser, signIn } from '@/test/utils'
import { useAuthStore } from '@/app/store/authStore'
import { visibleNavigation } from '@/components/layout/navigation'
import { routes } from './routes'

// Pages fetch on mount; these tests are about routing and guards, not data.
vi.mock('@/shared/api/http', () => {
  const emptyPage = { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 }
  const get = vi.fn().mockResolvedValue({ data: emptyPage })
  return {
    http: { get, post: vi.fn().mockResolvedValue({ data: {} }), put: vi.fn(), delete: vi.fn() },
    refreshClient: {},
    refreshAccessToken: vi.fn().mockResolvedValue(null),
    restoreSession: vi.fn().mockResolvedValue(false),
  }
})

function renderAt(path: string) {
  const router = createMemoryRouter(routes, { initialEntries: [path] })
  render(
    <QueryClientProvider client={createTestQueryClient()}>
      <ThemeProvider theme={createAppTheme('light')}>
        <RouterProvider router={router} />
      </ThemeProvider>
    </QueryClientProvider>,
  )
  return router
}

// Pages are lazy-loaded. Import them once up front so the tests below are not timed on cold module transforms.
beforeAll(async () => {
  await Promise.all(Object.values(import.meta.glob('../../features/**/pages/*Page.tsx')).map((load) => load()))
}, 120_000)

describe('route guards', () => {
  it('shows a loading state while the session is being restored', () => {
    renderAt('/settings/users')

    expect(screen.getByRole('status')).toHaveTextContent(/restoring your session/i)
  })

  it('sends anonymous visitors to sign in and remembers where they were going', async () => {
    useAuthStore.getState().markAnonymous()

    const router = renderAt('/settings/users?status=Locked')

    expect(await screen.findByRole('heading', { name: /welcome back/i })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/login')
    expect(router.state.location.state).toEqual({ from: '/settings/users?status=Locked' })
  })

  it('answers unknown URLs the same way as protected ones, so signed-out visitors cannot probe which pages exist', async () => {
    useAuthStore.getState().markAnonymous()

    const router = renderAt('/some/made/up/path')

    expect(await screen.findByRole('heading', { name: /welcome back/i })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/login')
  })

  it('shows signed-in users a not-found page inside the app', async () => {
    signIn()

    const router = renderAt('/some/made/up/path')

    expect(await screen.findByRole('heading', { name: /page not found/i })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/some/made/up/path')
    expect(screen.getByRole('link', { name: /skip to main content/i })).toBeInTheDocument() // the app shell is around it
  })

  it('keeps signed-in users away from the sign-in page', async () => {
    signIn()

    const router = renderAt('/login')

    expect(await screen.findByRole('heading', { name: /welcome back, ada|good (morning|afternoon|evening), ada/i })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe(paths.dashboard)
  })

  it('lets emailed links work regardless of session state', async () => {
    signIn()

    renderAt('/reset-password?token=abc')

    expect(await screen.findByRole('heading', { name: /choose a new password/i })).toBeInTheDocument()
  })

  it('shows a 403 page, not a redirect, when the user lacks the permission', async () => {
    signIn(makeUser({ permissions: ['User.Read'] }))

    const router = renderAt('/settings/roles')

    expect(await screen.findByRole('heading', { name: /you do not have access to this page/i })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/settings/roles')
  })

  it('lets a user with the permission through', async () => {
    signIn(makeUser({ permissions: ['Role.Read'] }))

    renderAt('/settings/roles')

    expect(await screen.findByRole('heading', { name: /roles & permissions/i })).toBeInTheDocument()
  })

  it('protects the audit log with Audit.Read', async () => {
    signIn(makeUser({ permissions: ['User.Read'] }))

    renderAt('/audit-logs')

    expect(await screen.findByRole('heading', { name: /you do not have access/i })).toBeInTheDocument()
  })

  it('keeps the platform registry for platform operators', async () => {
    signIn(makeUser())

    renderAt('/platform/organizations')

    expect(await screen.findByRole('heading', { name: /you do not have access/i })).toBeInTheDocument()
  })

  it('sends platform operators from the dashboard to the registry', async () => {
    signIn(makeUser({ isPlatformUser: true, organization: null, roles: ['SUPER_ADMIN'], permissions: ['Platform.Manage'] }))

    const router = renderAt('/dashboard')

    expect(await screen.findByRole('heading', { name: 'Organizations' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe(paths.platformOrganizations)
  })

  it('redirects the root and the bare settings URL to their real pages', async () => {
    signIn()

    const router = renderAt('/settings')

    await screen.findByRole('heading', { name: 'Organization' })
    expect(router.state.location.pathname).toBe(paths.organization)
  })
})

describe('route table', () => {
  it.each(Object.entries(paths).filter(([name]) => name !== 'forbidden'))('has a route for paths.%s', (_name, path) => {
    expect(matchRoutes(routes, path)).not.toBeNull()
  })

  it('every routed page is code-split (lazy)', () => {
    const flat: unknown[] = []
    const walk = (list: typeof routes) => list.forEach((r) => { flat.push(r); if (r.children) walk(r.children) })
    walk(routes)

    const pages = (flat as { path?: string; lazy?: unknown; element?: unknown; index?: boolean }[]).filter((r) => r.path && r.path !== 'settings')
    expect(pages.length).toBeGreaterThan(8)
    expect(pages.every((r) => typeof r.lazy === 'function')).toBe(true)
  })
})

const labels = (viewer: Parameters<typeof visibleNavigation>[0]) => visibleNavigation(viewer).flatMap((g) => g.items.map((i) => i.label))

describe('navigation visibility', () => {
  it('shows an organization administrator the settings pages', () => {
    const all = labels({ isPlatformUser: false, permissions: ['User.Read', 'Role.Read', 'Audit.Read'] })

    expect(all).toEqual(expect.arrayContaining(['Dashboard', 'Organization', 'Users', 'Roles & permissions', 'Audit log']))
    expect(all).not.toContain('Organizations')
  })

  it('hides pages the user has no permission for', () => {
    const all = labels({ isPlatformUser: false, permissions: [] })

    expect(all).toContain('Dashboard')
    expect(all).toContain('Organization')
    expect(all).not.toContain('Users')
    expect(all).not.toContain('Audit log')
    expect(all).not.toContain('Campaigns')
  })

  it('shows upcoming modules as disabled "soon" entries only to those who could use them', () => {
    const groups = visibleNavigation({ isPlatformUser: false, permissions: ['Campaign.Read'] })
    const campaigns = groups.flatMap((g) => g.items).find((i) => i.label === 'Campaigns')

    expect(campaigns).toMatchObject({ soon: true })
    expect(campaigns?.to).toBeUndefined()
  })

  it('shows platform operators only the platform area', () => {
    const all = labels({ isPlatformUser: true, permissions: ['Platform.Manage'] })

    expect(all).toEqual(['Organizations'])
  })
})
