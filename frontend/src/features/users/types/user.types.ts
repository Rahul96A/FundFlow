import type { ListParams } from '@/shared/types/api'

export const USER_STATUSES = ['Active', 'Invited', 'PendingVerification', 'Locked', 'Deactivated'] as const
export type UserStatus = (typeof USER_STATUSES)[number]

export interface UserSummary {
  id: string
  email: string
  firstName: string
  lastName: string
  fullName: string
  status: UserStatus
  roles: string[]
  lastLoginAt: string | null
  createdAt: string
}

export interface UserRoleRef {
  id: string
  name: string
}

export interface UserDetail {
  id: string
  email: string
  firstName: string
  lastName: string
  fullName: string
  phoneNumber: string | null
  status: UserStatus
  emailConfirmed: boolean
  isActive: boolean
  lockoutEnd: string | null
  lastLoginAt: string | null
  createdAt: string
  roles: UserRoleRef[]
}

export interface UserListParams extends ListParams {
  status?: UserStatus
  roleId?: string
}

export interface InviteUserInput {
  email: string
  firstName: string
  lastName: string
  phoneNumber: string | null
  roleIds: string[]
}

export interface UpdateUserInput {
  firstName: string
  lastName: string
  phoneNumber: string | null
}
