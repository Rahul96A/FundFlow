import type { AxiosAdapter, AxiosResponse, InternalAxiosRequestConfig } from 'axios'
import { AxiosError } from 'axios'
import { useAuthStore } from '@/app/store/authStore'
import { http, refreshAccessToken, refreshClient, restoreSession } from './http'

function response(config: InternalAxiosRequestConfig, status: number, data: unknown = {}): AxiosResponse {
  return { data, status, statusText: String(status), headers: {}, config }
}

function reject(config: InternalAxiosRequestConfig, status: number): never {
  throw new AxiosError('failed', String(status), config, null, response(config, status))
}

const tokenBody = (accessToken: string) => ({
  accessToken,
  tokenType: 'Bearer',
  expiresInSeconds: 900,
  expiresAt: new Date(Date.now() + 900_000).toISOString(),
})

describe('http client', () => {
  let apiCalls: { url: string | undefined; authorization: unknown }[]
  let refreshCalls: number
  let refreshShouldFail: boolean

  beforeEach(() => {
    apiCalls = []
    refreshCalls = 0
    refreshShouldFail = false
    useAuthStore.setState({ status: 'authenticated', accessToken: 'expired-token', expiresAt: 0, user: null })

    // API server: only "fresh-token" is accepted.
    const api: AxiosAdapter = async (config) => {
      apiCalls.push({ url: config.url, authorization: config.headers.Authorization })
      if (config.headers.Authorization === 'Bearer fresh-token') {
        return response(config, 200, { ok: true })
      }
      return reject(config, 401)
    }
    // Refresh endpoint.
    const refresh: AxiosAdapter = async (config) => {
      refreshCalls++
      await new Promise((resolve) => setTimeout(resolve, 10))
      return refreshShouldFail ? reject(config, 401) : response(config, 200, tokenBody('fresh-token'))
    }
    http.defaults.adapter = api
    refreshClient.defaults.adapter = refresh
  })

  it('attaches the in-memory access token to requests', async () => {
    useAuthStore.setState({ accessToken: 'fresh-token' })

    await http.get('/users')

    expect(apiCalls).toEqual([{ url: '/users', authorization: 'Bearer fresh-token' }])
  })

  it('renews the token once on a 401 and transparently retries the request', async () => {
    const result = await http.get('/users')

    expect(result.data).toEqual({ ok: true })
    expect(refreshCalls).toBe(1)
    expect(apiCalls.map((c) => c.authorization)).toEqual(['Bearer expired-token', 'Bearer fresh-token'])
    expect(useAuthStore.getState().accessToken).toBe('fresh-token')
  })

  it('shares a single refresh between concurrent requests (single-flight)', async () => {
    await Promise.all([http.get('/a'), http.get('/b'), http.get('/c')])

    expect(refreshCalls).toBe(1)
    expect(apiCalls.filter((c) => c.authorization === 'Bearer fresh-token')).toHaveLength(3)
  })

  it('signs the user out when the session cannot be renewed', async () => {
    refreshShouldFail = true

    await expect(http.get('/users')).rejects.toMatchObject({ response: { status: 401 } })

    expect(useAuthStore.getState().status).toBe('anonymous')
    expect(useAuthStore.getState().accessToken).toBeNull()
  })

  it('does not try to refresh for requests that never carried a token (e.g. a wrong password)', async () => {
    useAuthStore.setState({ accessToken: null })

    await expect(http.post('/auth/login', {})).rejects.toMatchObject({ response: { status: 401 } })

    expect(refreshCalls).toBe(0)
    expect(apiCalls).toHaveLength(1)
  })

  it('retries at most once so a genuinely unauthorised endpoint cannot loop', async () => {
    http.defaults.adapter = async (config) => {
      apiCalls.push({ url: config.url, authorization: config.headers.Authorization })
      return reject(config, 401)
    }

    await expect(http.get('/users')).rejects.toBeTruthy()

    expect(apiCalls).toHaveLength(2)
    expect(refreshCalls).toBe(1)
  })

  it('restores a session from the refresh cookie at startup', async () => {
    useAuthStore.setState({ status: 'loading', accessToken: null })

    expect(await restoreSession()).toBe(true)
    expect(useAuthStore.getState().accessToken).toBe('fresh-token')
  })

  it('reports no session when the refresh cookie is missing or expired', async () => {
    refreshShouldFail = true

    expect(await refreshAccessToken()).toBeNull()
    expect(await restoreSession()).toBe(false)
  })

  it('never sends the CSRF header value from script-controlled input', () => {
    expect(http.defaults.headers['X-FundFlow-Client']).toBe('web')
    expect(http.defaults.withCredentials).toBe(true)
    expect(http.defaults.baseURL).toBe('/api/v1')
  })
})
