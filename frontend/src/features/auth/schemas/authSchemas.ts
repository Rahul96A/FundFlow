import { z } from 'zod'

/** Mirrors the API's password policy (ValidationRules.MustBeStrongPassword): the server is still the authority. */
export const PASSWORD_MIN_LENGTH = 12
export const PASSWORD_MAX_LENGTH = 128

export interface PasswordCheck {
  id: string
  label: string
  ok: boolean
}

export function passwordChecks(password: string): PasswordCheck[] {
  return [
    { id: 'length', label: `At least ${PASSWORD_MIN_LENGTH} characters`, ok: password.length >= PASSWORD_MIN_LENGTH },
    { id: 'lower', label: 'A lowercase letter', ok: /[a-z]/.test(password) },
    { id: 'upper', label: 'An uppercase letter', ok: /[A-Z]/.test(password) },
    { id: 'digit', label: 'A number', ok: /\d/.test(password) },
  ]
}

const email = z
  .string()
  .trim()
  .min(1, 'Enter your email address.')
  .max(254, 'That email address is too long.')
  .refine((value) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value), 'Enter a valid email address.')

export const newPassword = z
  .string()
  .min(1, 'Choose a password.')
  .min(PASSWORD_MIN_LENGTH, `Use at least ${PASSWORD_MIN_LENGTH} characters.`)
  .max(PASSWORD_MAX_LENGTH, `Use at most ${PASSWORD_MAX_LENGTH} characters.`)
  .regex(/[a-z]/, 'Include a lowercase letter.')
  .regex(/[A-Z]/, 'Include an uppercase letter.')
  .regex(/\d/, 'Include a number.')

const personName = (label: string) =>
  z.string().trim().min(1, `Enter ${label}.`).max(100, `${label.charAt(0).toUpperCase()}${label.slice(1)} is too long.`)

export const loginSchema = z.object({
  email,
  password: z.string().min(1, 'Enter your password.').max(256),
})
export type LoginForm = z.infer<typeof loginSchema>

export const registerSchema = z
  .object({
    organizationName: z.string().trim().min(1, 'Enter your organization name.').max(200, 'That name is too long.'),
    firstName: personName('your first name'),
    lastName: personName('your last name'),
    email,
    password: newPassword,
    confirmPassword: z.string().min(1, 'Repeat your password.'),
    currencyCode: z.string().length(3, 'Choose a currency.'),
  })
  .refine((v) => v.password === v.confirmPassword, { path: ['confirmPassword'], message: 'Passwords do not match.' })
export type RegisterForm = z.infer<typeof registerSchema>

export const forgotPasswordSchema = z.object({ email })
export type ForgotPasswordForm = z.infer<typeof forgotPasswordSchema>

export const resetPasswordSchema = z
  .object({ newPassword, confirmPassword: z.string().min(1, 'Repeat your password.') })
  .refine((v) => v.newPassword === v.confirmPassword, { path: ['confirmPassword'], message: 'Passwords do not match.' })
export type ResetPasswordForm = z.infer<typeof resetPasswordSchema>

export const acceptInvitationSchema = z
  .object({
    firstName: z.string().trim().max(100, 'That name is too long.'),
    lastName: z.string().trim().max(100, 'That name is too long.'),
    password: newPassword,
    confirmPassword: z.string().min(1, 'Repeat your password.'),
  })
  .refine((v) => v.password === v.confirmPassword, { path: ['confirmPassword'], message: 'Passwords do not match.' })
export type AcceptInvitationForm = z.infer<typeof acceptInvitationSchema>

export const changePasswordSchema = z
  .object({
    currentPassword: z.string().min(1, 'Enter your current password.'),
    newPassword,
    confirmPassword: z.string().min(1, 'Repeat your new password.'),
  })
  .refine((v) => v.newPassword === v.confirmPassword, { path: ['confirmPassword'], message: 'Passwords do not match.' })
  .refine((v) => v.newPassword !== v.currentPassword, {
    path: ['newPassword'],
    message: 'Choose a password different from your current one.',
  })
export type ChangePasswordForm = z.infer<typeof changePasswordSchema>

const phone = z
  .string()
  .trim()
  .max(30, 'That phone number is too long.')
  .refine((v) => v === '' || /^[+()\-.\s0-9xX]{5,30}$/.test(v), 'Enter a valid phone number.')

export const profileSchema = z.object({
  firstName: personName('your first name'),
  lastName: personName('your last name'),
  phoneNumber: phone,
})
export type ProfileForm = z.infer<typeof profileSchema>
