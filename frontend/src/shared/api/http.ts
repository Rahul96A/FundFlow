import axios, { type AxiosError, type InternalAxiosRequestConfig } from 'axios'
import { getAccessToken, useAuthStore } from '@/app/store/authStore'
import type { TokenResponse } from '@/shared/types/api'

const defaults = {
  baseURL: '/api/v1',
  // The refresh token is an HttpOnly cookie; the browser attaches it, script never sees it.
  withCredentials: true,
  timeout: 60_000, // a paused serverless database can take most of a minute to resume on the first request
  // Required by the API on cookie-authenticated endpoints (CSRF defence-in-depth).
  headers: { 'X-FundFlow-Client': 'web' },
}

/** The application's HTTP client: attaches the access token and transparently renews it once on a 401. */
export const http = axios.create(defaults)

/** Interceptor-free client for the refresh call itself (an interceptor here would recurse). Exported for tests. */
export const refreshClient = axios.create(defaults)

http.interceptors.request.use((config) => {
  const token = getAccessToken()
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

type RetriableConfig = InternalAxiosRequestConfig & { retriedAfterRefresh?: boolean }

let inFlight: Promise<string | null> | null = null

/**
 * Exchanges the refresh cookie for a new access token. Concurrent callers share one request (single-flight), and a
 * Web Lock serialises refreshes across browser tabs: the refresh cookie is shared by all tabs and rotates on use,
 * so two tabs refreshing at the same instant would otherwise present the same token twice.
 */
export function refreshAccessToken(): Promise<string | null> {
  inFlight ??= withCrossTabLock(async () => {
    try {
      const { data } = await refreshClient.post<TokenResponse>('/auth/refresh')
      useAuthStore.getState().setToken(data)
      return data.accessToken
    } catch {
      return null
    }
  }).finally(() => {
    inFlight = null
  })
  return inFlight
}

async function withCrossTabLock<T>(work: () => Promise<T>): Promise<T> {
  if (typeof navigator !== 'undefined' && 'locks' in navigator) {
    return (await navigator.locks.request('fundflow-refresh', work)) as T
  }
  return work()
}

http.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const original = error.config as RetriableConfig | undefined
    const wasAuthenticatedRequest = Boolean(original?.headers?.Authorization)

    // Only renew for requests that carried a token: an anonymous 401 (wrong password) is a real answer.
    if (error.response?.status === 401 && original && wasAuthenticatedRequest && !original.retriedAfterRefresh) {
      original.retriedAfterRefresh = true
      const token = await refreshAccessToken()
      if (token) {
        original.headers.Authorization = `Bearer ${token}`
        return http(original)
      }
      // The session is gone. Route guards react to this and send the user to sign in.
      useAuthStore.getState().signOut()
    }
    return Promise.reject(error)
  },
)

/** Starts the session from the refresh cookie (used at page load). Returns false when there is no valid session. */
export async function restoreSession(): Promise<boolean> {
  const token = await refreshAccessToken()
  return token !== null
}
