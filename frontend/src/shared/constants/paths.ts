/** Every route in one place, so links and redirects cannot drift from the router. */
export const paths = {
  root: '/',
  login: '/login',
  register: '/register',
  forgotPassword: '/forgot-password',
  resetPassword: '/reset-password',
  verifyEmail: '/verify-email',
  acceptInvitation: '/accept-invitation',
  dashboard: '/dashboard',
  users: '/settings/users',
  roles: '/settings/roles',
  organization: '/settings/organization',
  account: '/account',
  auditLogs: '/audit-logs',
  platformOrganizations: '/platform/organizations',
  forbidden: '/forbidden',
} as const

export type AppPath = (typeof paths)[keyof typeof paths]
