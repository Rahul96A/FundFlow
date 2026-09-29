import { create } from 'zustand'
import type { CurrentUser } from '@/features/auth/types/auth.types'
import type { TokenResponse } from '@/shared/types/api'

/**
 * `loading`      the app has not yet learned whether a session exists (silent refresh in flight)
 * `authenticated` an access token and user profile are held
 * `anonymous`    no session
 */
export type AuthStatus = 'loading' | 'authenticated' | 'anonymous'

interface AuthState {
  status: AuthStatus
  /** Held in memory only: never localStorage, so an XSS payload cannot lift it from storage. */
  accessToken: string | null
  expiresAt: number | null
  user: CurrentUser | null
  setToken: (token: TokenResponse) => void
  setUser: (user: CurrentUser) => void
  signOut: () => void
  markAnonymous: () => void
}

export const useAuthStore = create<AuthState>()((set) => ({
  status: 'loading',
  accessToken: null,
  expiresAt: null,
  user: null,
  setToken: (token) => set({ accessToken: token.accessToken, expiresAt: Date.parse(token.expiresAt) }),
  setUser: (user) => set({ user, status: 'authenticated' }),
  signOut: () => set({ status: 'anonymous', accessToken: null, expiresAt: null, user: null }),
  markAnonymous: () => set({ status: 'anonymous', accessToken: null, expiresAt: null, user: null }),
}))

/** For non-React code (the HTTP layer) that needs the current token without subscribing. */
export const getAccessToken = (): string | null => useAuthStore.getState().accessToken

export function hasPermission(user: CurrentUser | null, permission: string): boolean {
  return user?.permissions.includes(permission) ?? false
}

/** Subscribes to a single permission check; re-renders only when the answer changes. */
export function usePermission(permission: string): boolean {
  return useAuthStore((state) => hasPermission(state.user, permission))
}

export function useHasAnyPermission(permissions: readonly string[]): boolean {
  return useAuthStore((state) => permissions.some((p) => hasPermission(state.user, p)))
}
