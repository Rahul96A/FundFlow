import { http } from '@/shared/api/http'
import type { TokenResponse } from '@/shared/types/api'
import type {
  AcceptInvitationInput,
  ChangePasswordInput,
  CurrentUser,
  LoginInput,
  RegisterOrganizationInput,
  RegisterOrganizationResult,
  UpdateProfileInput,
  UserSession,
} from '../types/auth.types'

/** Thin, typed wrappers over the /auth endpoints. No state, no side effects: hooks own those. */
export const authApi = {
  login: (input: LoginInput) => http.post<TokenResponse>('/auth/login', input).then((r) => r.data),

  logout: () => http.post('/auth/logout').then(() => undefined),

  me: () => http.get<CurrentUser>('/auth/me').then((r) => r.data),

  registerOrganization: (input: RegisterOrganizationInput) =>
    http.post<RegisterOrganizationResult>('/auth/register-organization', input).then((r) => r.data),

  forgotPassword: (email: string) => http.post('/auth/forgot-password', { email }).then(() => undefined),

  resetPassword: (token: string, newPassword: string) =>
    http.post('/auth/reset-password', { token, newPassword }).then(() => undefined),

  verifyEmail: (token: string) => http.post('/auth/verify-email', { token }).then(() => undefined),

  resendVerification: (email: string) => http.post('/auth/resend-verification', { email }).then(() => undefined),

  acceptInvitation: (input: AcceptInvitationInput) => http.post('/auth/accept-invitation', input).then(() => undefined),

  changePassword: (input: ChangePasswordInput) => http.post('/auth/change-password', input).then(() => undefined),

  updateProfile: (input: UpdateProfileInput) => http.put<CurrentUser>('/auth/me', input).then((r) => r.data),

  sessions: () => http.get<UserSession[]>('/auth/sessions').then((r) => r.data),

  revokeSession: (id: string) => http.delete(`/auth/sessions/${id}`).then(() => undefined),
}
