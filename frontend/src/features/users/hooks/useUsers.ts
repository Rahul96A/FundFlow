import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { organizationKeys } from '@/features/organization/hooks/useOrganization'
import { roleKeys } from '@/features/roles/hooks/useRoles'
import { usersApi } from '../api/usersApi'
import type { InviteUserInput, UpdateUserInput, UserListParams } from '../types/user.types'

export const userKeys = {
  all: ['users'] as const,
  list: (params: UserListParams) => [...userKeys.all, 'list', params] as const,
  detail: (id: string) => [...userKeys.all, 'detail', id] as const,
}

export function useUsers(params: UserListParams) {
  return useQuery({
    queryKey: userKeys.list(params),
    queryFn: () => usersApi.list(params),
    placeholderData: keepPreviousData, // keep the old page on screen while the next loads: no flicker
  })
}

export function useUser(id: string | null) {
  return useQuery({
    queryKey: userKeys.detail(id ?? ''),
    queryFn: () => usersApi.get(id!),
    enabled: id !== null,
  })
}

/** Anything that changes who is on the team also changes the dashboard counts and role user-counts. */
function useInvalidateTeam() {
  const queryClient = useQueryClient()
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: userKeys.all }),
      queryClient.invalidateQueries({ queryKey: organizationKeys.overview() }),
      queryClient.invalidateQueries({ queryKey: roleKeys.all }),
    ])
}

export function useInviteUser() {
  const invalidate = useInvalidateTeam()
  return useMutation({
    meta: { silent: true },
    mutationFn: (input: InviteUserInput) => usersApi.invite(input),
    onSuccess: invalidate,
  })
}

export function useUpdateUser() {
  const invalidate = useInvalidateTeam()
  return useMutation({
    meta: { silent: true },
    mutationFn: ({ id, input }: { id: string; input: UpdateUserInput }) => usersApi.update(id, input),
    onSuccess: invalidate,
  })
}

export function useSetUserRoles() {
  const invalidate = useInvalidateTeam()
  return useMutation({
    meta: { silent: true },
    mutationFn: ({ id, roleIds }: { id: string; roleIds: string[] }) => usersApi.setRoles(id, roleIds),
    onSuccess: invalidate,
  })
}

type UserAction = 'deactivate' | 'reactivate' | 'unlock' | 'resendInvitation'

/** One mutation for the simple lifecycle actions; errors surface as a toast. */
export function useUserAction() {
  const invalidate = useInvalidateTeam()
  return useMutation({
    mutationFn: ({ id, action }: { id: string; action: UserAction }) => usersApi[action](id),
    onSuccess: invalidate,
  })
}
