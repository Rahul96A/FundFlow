import { safeReturnPath } from './returnPath'

describe('safeReturnPath', () => {
  it('keeps same-site absolute paths, including query strings', () => {
    expect(safeReturnPath('/settings/users?status=Active', '/dashboard')).toBe('/settings/users?status=Active')
  })

  it.each([
    'https://evil.example/steal',
    '//evil.example',
    '/\\evil.example',
    'javascript:alert(1)',
    'dashboard',
    '',
  ])('rejects %j to prevent an open redirect', (candidate) => {
    expect(safeReturnPath(candidate, '/dashboard')).toBe('/dashboard')
  })

  it('rejects non-strings and never bounces back to the login page', () => {
    expect(safeReturnPath(undefined, '/dashboard')).toBe('/dashboard')
    expect(safeReturnPath({ from: '/x' }, '/dashboard')).toBe('/dashboard')
    expect(safeReturnPath('/login', '/dashboard')).toBe('/dashboard')
    expect(safeReturnPath('/login?x=1', '/dashboard')).toBe('/dashboard')
  })
})
