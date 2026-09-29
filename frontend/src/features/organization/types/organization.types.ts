export interface Address {
  line1: string | null
  line2: string | null
  city: string | null
  region: string | null
  postalCode: string | null
  country: string | null
}

export interface OrganizationSettings {
  timeZoneId: string
  currencyCode: string
  locale: string
  fiscalYearStartMonth: number
  logoUrl: string | null
  brandColor: string | null
}

export type OrganizationStatus = 'Active' | 'Suspended'

export interface Organization {
  id: string
  name: string
  slug: string
  legalName: string | null
  taxId: string | null
  website: string | null
  contactEmail: string
  phoneNumber: string | null
  address: Address
  status: OrganizationStatus
  settings: OrganizationSettings
  createdAt: string
}

export interface UpdateOrganizationInput {
  name: string
  legalName: string | null
  taxId: string | null
  website: string | null
  contactEmail: string
  phoneNumber: string | null
  address: Address
}

export type UpdateSettingsInput = OrganizationSettings

export interface TeamOverview {
  totalUsers: number
  activeUsers: number
  pendingInvitations: number
  roleCount: number
}

export interface ActivityItem {
  id: string
  timestamp: string
  action: string
  entityType: string
  entityId: string | null
  userEmail: string | null
}

export interface ActivityPoint {
  date: string
  count: number
}

export interface OrganizationOverview {
  profileCompleted: boolean
  team: TeamOverview | null
  activity: { recent: ActivityItem[]; byDay: ActivityPoint[] } | null
}
