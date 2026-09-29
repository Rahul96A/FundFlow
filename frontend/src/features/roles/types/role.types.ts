export interface RoleSummary {
  id: string
  name: string
  description: string | null
  isSystem: boolean
  permissionCount: number
  userCount: number
}

export interface RoleDetail {
  id: string
  name: string
  description: string | null
  isSystem: boolean
  permissions: string[]
  userCount: number
}

export interface PermissionInfo {
  name: string
  module: string
  description: string
}

export interface PermissionGroup {
  module: string
  permissions: PermissionInfo[]
}

export interface SaveRoleInput {
  name: string
  description: string | null
  permissions: string[]
}
