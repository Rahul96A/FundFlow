import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { rolesApi } from '../api/rolesApi'
import type { SaveRoleInput } from '../types/role.types'

export const roleKeys = {
  all: ['roles'] as const,
  list: () => [...roleKeys.all, 'list'] as const,
  detail: (id: string) => [...roleKeys.all, 'detail', id] as const,
  permissions: () => ['permissions'] as const,
}

export function useRoles(enabled = true) {
  return useQuery({ queryKey: roleKeys.list(), queryFn: rolesApi.list, enabled })
}

export function useRole(id: string | null) {
  return useQuery({ queryKey: roleKeys.detail(id ?? ''), queryFn: () => rolesApi.get(id!), enabled: id !== null })
}

/** The catalogue changes only with a release, so it is safe to cache for the whole session. */
export function usePermissionCatalog(enabled = true) {
  return useQuery({ queryKey: roleKeys.permissions(), queryFn: rolesApi.permissions, staleTime: Infinity, enabled })
}

export function useCreateRole() {
  const queryClient = useQueryClient()
  return useMutation({
    meta: { silent: true },
    mutationFn: (input: SaveRoleInput) => rolesApi.create(input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: roleKeys.all }),
  })
}

export function useUpdateRole() {
  const queryClient = useQueryClient()
  return useMutation({
    meta: { silent: true },
    mutationFn: ({ id, input }: { id: string; input: SaveRoleInput }) => rolesApi.update(id, input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: roleKeys.all }),
  })
}

export function useDeleteRole() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => rolesApi.remove(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: roleKeys.all }),
  })
}
