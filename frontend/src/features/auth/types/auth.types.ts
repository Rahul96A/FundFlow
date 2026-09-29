export interface CurrentOrganization {
  id: string
  name: string
  slug: string
  timeZoneId: string
  currencyCode: string
  locale: string
  logoUrl: string | null
  brandColor: string | null
}

export interface CurrentUser {
  id: string
  email: string
  firstName: string
  lastName: string
  fullName: string
  phoneNumber: string | null
  emailConfirmed: boolean
  isPlatformUser: boolean
  roles: string[]
  permissions: string[]
  organization: CurrentOrganization | null
}

export interface RegisterOrganizationInput {
  organizationName: string
  organizationSlug?: string | null
  firstName: string
  lastName: string
  email: string
  password: string
  timeZoneId?: string | null
  currencyCode?: string | null
}

export interface RegisterOrganizationResult {
  organizationId: string
  slug: string
  email: string
  requiresEmailVerification: boolean
}

export interface LoginInput {
  email: string
  password: string
}

export interface AcceptInvitationInput {
  token: string
  password: string
  firstName?: string | null
  lastName?: string | null
}

export interface ChangePasswordInput {
  currentPassword: string
  newPassword: string
}

export interface UpdateProfileInput {
  firstName: string
  lastName: string
  phoneNumber: string | null
}

export interface UserSession {
  id: string
  ipAddress: string | null
  userAgent: string | null
  createdAt: string
  lastSeenAt: string
  expiresAt: string
  isCurrent: boolean
}
