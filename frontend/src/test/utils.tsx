import { ThemeProvider } from '@mui/material/styles'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, type RenderOptions } from '@testing-library/react'
import type { ReactElement, ReactNode } from 'react'
import { MemoryRouter, type InitialEntry } from 'react-router'
import { useAuthStore } from '@/app/store/authStore'
import { createAppTheme } from '@/app/theme/theme'
import { ConfirmHost } from '@/components/ui/ConfirmDialog'
import { ToastHost } from '@/components/ui/Toast'
import type { CurrentUser } from '@/features/auth/types/auth.types'

export function makeUser(overrides: Partial<CurrentUser> = {}): CurrentUser {
  return {
    id: 'user-1',
    email: 'ada@hope.test',
    firstName: 'Ada',
    lastName: 'Lovelace',
    fullName: 'Ada Lovelace',
    phoneNumber: null,
    emailConfirmed: true,
    isPlatformUser: false,
    roles: ['ORGANIZATION_ADMIN'],
    permissions: ['User.Read', 'User.Manage', 'Role.Read', 'Role.Manage', 'Audit.Read', 'Organization.Update'],
    organization: {
      id: 'org-1',
      name: 'Hope Foundation',
      slug: 'hope-foundation',
      timeZoneId: 'America/New_York',
      currencyCode: 'USD',
      locale: 'en-US',
      logoUrl: null,
      brandColor: null,
    },
    ...overrides,
  }
}

/** Puts the app into the signed-in state without going through the network. */
export function signIn(user: CurrentUser = makeUser()) {
  useAuthStore.setState({ status: 'authenticated', accessToken: 'test-token', expiresAt: Date.now() + 900_000, user })
}

export function createTestQueryClient() {
  return new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity }, mutations: { retry: false } } })
}

interface Options extends Omit<RenderOptions, 'wrapper'> {
  route?: InitialEntry
  queryClient?: QueryClient
}

/** Renders with the same providers the real app has: router, query cache, theme, toasts and confirmations. */
export function renderWithProviders(ui: ReactElement, { route = '/', queryClient = createTestQueryClient(), ...options }: Options = {}) {
  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        <ThemeProvider theme={createAppTheme('light')}>
          <MemoryRouter initialEntries={[route]}>{children}</MemoryRouter>
          <ToastHost />
          <ConfirmHost />
        </ThemeProvider>
      </QueryClientProvider>
    )
  }
  return { queryClient, ...render(ui, { wrapper: Wrapper, ...options }) }
}
