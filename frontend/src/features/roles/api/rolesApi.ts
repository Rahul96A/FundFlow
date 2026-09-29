import { http } from '@/shared/api/http'
import type { PermissionGroup, RoleDetail, RoleSummary, SaveRoleInput } from '../types/role.types'

export const rolesApi = {
  list: () => http.get<RoleSummary[]>('/roles').then((r) => r.data),

  get: (id: string) => http.get<RoleDetail>(`/roles/${id}`).then((r) => r.data),

  create: (input: SaveRoleInput) => http.post<RoleDetail>('/roles', input).then((r) => r.data),

  update: (id: string, input: SaveRoleInput) => http.put<RoleDetail>(`/roles/${id}`, input).then((r) => r.data),

  remove: (id: string) => http.delete(`/roles/${id}`).then(() => undefined),

  permissions: () => http.get<PermissionGroup[]>('/permissions').then((r) => r.data),
}
