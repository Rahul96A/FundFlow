import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { refreshCurrentUser } from '@/features/auth/hooks/useAuthActions'
import { organizationApi } from '../api/organizationApi'
import type { UpdateOrganizationInput, UpdateSettingsInput } from '../types/organization.types'

export const organizationKeys = {
  all: ['organization'] as const,
  detail: () => [...organizationKeys.all, 'detail'] as const,
  overview: () => [...organizationKeys.all, 'overview'] as const,
}

export function useOrganization() {
  return useQuery({ queryKey: organizationKeys.detail(), queryFn: organizationApi.get })
}

export function useOverview() {
  return useQuery({ queryKey: organizationKeys.overview(), queryFn: organizationApi.overview })
}

export function useUpdateOrganization() {
  const queryClient = useQueryClient()
  return useMutation({
    meta: { silent: true },
    mutationFn: (input: UpdateOrganizationInput) => organizationApi.update(input),
    onSuccess: async (organization) => {
      queryClient.setQueryData(organizationKeys.detail(), organization)
      await queryClient.invalidateQueries({ queryKey: organizationKeys.overview() })
      await refreshCurrentUser() // the header shows the organization name
    },
  })
}

export function useUpdateSettings() {
  const queryClient = useQueryClient()
  return useMutation({
    meta: { silent: true },
    mutationFn: (input: UpdateSettingsInput) => organizationApi.updateSettings(input),
    onSuccess: async (organization) => {
      queryClient.setQueryData(organizationKeys.detail(), organization)
      await refreshCurrentUser() // timezone and currency drive formatting everywhere
    },
  })
}
