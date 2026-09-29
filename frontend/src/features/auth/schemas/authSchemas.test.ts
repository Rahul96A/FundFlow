import {
  acceptInvitationSchema,
  changePasswordSchema,
  loginSchema,
  newPassword,
  passwordChecks,
  profileSchema,
  registerSchema,
} from './authSchemas'

const valid = 'Correct-Horse-Battery9'

describe('password policy', () => {
  it('accepts a long mixed-case password with a digit', () => {
    expect(newPassword.safeParse(valid).success).toBe(true)
  })

  it.each([
    ['', 'Choose a password.'],
    ['Short1Aa', 'at least 12'],
    ['alllowercase1234', 'uppercase'],
    ['ALLUPPERCASE1234', 'lowercase'],
    ['NoDigitsHereAtAll', 'number'],
    [`Aa1${'x'.repeat(200)}`, 'at most'],
  ])('rejects %j with a helpful message', (password, hint) => {
    const result = newPassword.safeParse(password)
    expect(result.success).toBe(false)
    expect(result.error?.issues.map((i) => i.message).join(' ')).toContain(hint)
  })

  it('reports each rule live for the checklist', () => {
    const checks = Object.fromEntries(passwordChecks('abc').map((c) => [c.id, c.ok]))
    expect(checks).toEqual({ length: false, lower: true, upper: false, digit: false })
    expect(passwordChecks(valid).every((c) => c.ok)).toBe(true)
  })
})

describe('forms', () => {
  const registration = {
    organizationName: 'Hope Foundation',
    firstName: 'Ada',
    lastName: 'Lovelace',
    email: 'ada@hope.org',
    password: valid,
    confirmPassword: valid,
    currencyCode: 'USD',
  }

  it('accepts a complete registration', () => {
    expect(registerSchema.safeParse(registration).success).toBe(true)
  })

  it('requires the passwords to match and points at the confirmation field', () => {
    const result = registerSchema.safeParse({ ...registration, confirmPassword: 'different' })
    expect(result.success).toBe(false)
    expect(result.error?.issues[0]?.path).toEqual(['confirmPassword'])
  })

  it.each(['', 'no-at-sign', 'missing@tld', 'a b@c.org'])('rejects the email %j', (email) => {
    expect(loginSchema.safeParse({ email, password: 'x' }).success).toBe(false)
  })

  it('does not enforce the password policy at sign-in (only that something was typed)', () => {
    expect(loginSchema.safeParse({ email: 'ada@hope.org', password: 'weak' }).success).toBe(true)
    expect(loginSchema.safeParse({ email: 'ada@hope.org', password: '' }).success).toBe(false)
  })

  it('rejects reusing the current password when changing it', () => {
    const result = changePasswordSchema.safeParse({ currentPassword: valid, newPassword: valid, confirmPassword: valid })
    expect(result.success).toBe(false)
    expect(result.error?.issues.some((i) => i.path[0] === 'newPassword')).toBe(true)
  })

  it('treats first and last name as optional when accepting an invitation', () => {
    expect(acceptInvitationSchema.safeParse({ firstName: '', lastName: '', password: valid, confirmPassword: valid }).success).toBe(true)
  })

  it.each([
    ['+1 (555) 010-0100', true],
    ['', true],
    ['call me maybe', false],
  ])('validates the phone number %j', (phoneNumber, ok) => {
    expect(profileSchema.safeParse({ firstName: 'A', lastName: 'B', phoneNumber }).success).toBe(ok)
  })
})
