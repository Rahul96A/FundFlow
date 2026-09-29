import { http } from '@/shared/api/http'
import type { Paged } from '@/shared/types/api'
import type { InviteUserInput, UpdateUserInput, UserDetail, UserListParams, UserSummary } from '../types/user.types'

function clean(params: UserListParams): Record<string, string | number> {
  return Object.fromEntries(
    Object.entries(params).filter(([, value]) => value !== undefined && value !== ''),
  ) as Record<string, string | number>
}

export const usersApi = {
  list: (params: UserListParams) => http.get<Paged<UserSummary>>('/users', { params: clean(params) }).then((r) => r.data),

  get: (id: string) => http.get<UserDetail>(`/users/${id}`).then((r) => r.data),

  invite: (input: InviteUserInput) => http.post<UserDetail>('/users', input).then((r) => r.data),

  update: (id: string, input: UpdateUserInput) => http.put<UserDetail>(`/users/${id}`, input).then((r) => r.data),

  setRoles: (id: string, roleIds: string[]) => http.put<UserDetail>(`/users/${id}/roles`, { roleIds }).then((r) => r.data),

  deactivate: (id: string) => http.post(`/users/${id}/deactivate`).then(() => undefined),

  reactivate: (id: string) => http.post(`/users/${id}/reactivate`).then(() => undefined),

  unlock: (id: string) => http.post(`/users/${id}/unlock`).then(() => undefined),

  resendInvitation: (id: string) => http.post(`/users/${id}/resend-invitation`).then(() => undefined),
}
