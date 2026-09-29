import { useMutation } from '@tanstack/react-query'
import { useAuthStore } from '@/app/store/authStore'
import { restoreSession } from '@/shared/api/http'
import { queryClient } from '@/shared/api/queryClient'
import { authApi } from '../api/authApi'
import type { CurrentUser, LoginInput } from '../types/auth.types'

/** Loads the profile for the token already held and marks the session authenticated. */
export async function loadCurrentUser(): Promise<CurrentUser> {
  const user = await authApi.me()
  useAuthStore.getState().setUser(user)
  return user
}

/**
 * Called once at startup. If the refresh cookie is still good the user is signed back in silently; otherwise they
 * are anonymous and the router sends them to the sign-in page.
 */
export async function bootstrapSession(): Promise<void> {
  const { markAnonymous } = useAuthStore.getState()
  try {
    if (await restoreSession()) {
      await loadCurrentUser()
      return
    }
  } catch {
    // fall through: any failure to restore means "not signed in"
  }
  markAnonymous()
}

/** Login is handled inline by the form (it shows specific messages), so the global error toast is suppressed. */
export function useLogin() {
  return useMutation({
    meta: { silent: true },
    mutationFn: async (input: LoginInput) => {
      const token = await authApi.login(input)
      useAuthStore.getState().setToken(token)
      return loadCurrentUser()
    },
    onError: () => useAuthStore.getState().markAnonymous(),
  })
}

export function useSignOut() {
  return useMutation({
    meta: { silent: true },
    mutationFn: async () => {
      try {
        await authApi.logout()
      } catch {
        // Even if the server cannot be reached the local session must end.
      }
    },
    onSettled: () => {
      useAuthStore.getState().signOut()
      queryClient.clear()
    },
  })
}

/** Re-reads the profile (after the user edits it, or an admin changes their roles). */
export async function refreshCurrentUser(): Promise<void> {
  await loadCurrentUser()
}
