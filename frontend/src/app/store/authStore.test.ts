import { makeUser } from '@/test/utils'
import { getAccessToken, hasPermission, useAuthStore } from './authStore'

describe('auth store', () => {
  it('starts in the loading state, with no token or user', () => {
    const state = useAuthStore.getState()
    expect(state.status).toBe('loading')
    expect(getAccessToken()).toBeNull()
    expect(state.user).toBeNull()
  })

  it('holds the access token in memory only', () => {
    useAuthStore.getState().setToken({ accessToken: 'abc', tokenType: 'Bearer', expiresInSeconds: 900, expiresAt: '2030-01-01T00:00:00Z' })

    expect(getAccessToken()).toBe('abc')
    expect(useAuthStore.getState().expiresAt).toBe(Date.parse('2030-01-01T00:00:00Z'))
    expect(JSON.stringify({ ...window.localStorage })).not.toContain('abc')
    expect(JSON.stringify({ ...window.sessionStorage })).not.toContain('abc')
  })

  it('becomes authenticated when the profile is set and anonymous on sign-out', () => {
    useAuthStore.getState().setUser(makeUser())
    expect(useAuthStore.getState().status).toBe('authenticated')

    useAuthStore.getState().signOut()
    const state = useAuthStore.getState()
    expect(state.status).toBe('anonymous')
    expect(state.accessToken).toBeNull()
    expect(state.user).toBeNull()
  })

  it('checks permissions on the current user', () => {
    const user = makeUser({ permissions: ['User.Read'] })

    expect(hasPermission(user, 'User.Read')).toBe(true)
    expect(hasPermission(user, 'User.Manage')).toBe(false)
    expect(hasPermission(null, 'User.Read')).toBe(false)
  })
})
