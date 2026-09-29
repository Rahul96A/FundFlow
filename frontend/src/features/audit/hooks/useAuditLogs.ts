import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { http } from '@/shared/api/http'
import type { Paged } from '@/shared/types/api'
import type { AuditListParams, AuditLogEntry } from '../types/audit.types'

export const auditKeys = {
  all: ['audit-logs'] as const,
  list: (params: AuditListParams) => [...auditKeys.all, params] as const,
}

function clean(params: AuditListParams) {
  return Object.fromEntries(Object.entries(params).filter(([, value]) => value !== undefined && value !== ''))
}

export function useAuditLogs(params: AuditListParams) {
  return useQuery({
    queryKey: auditKeys.list(params),
    queryFn: () => http.get<Paged<AuditLogEntry>>('/audit-logs', { params: clean(params) }).then((r) => r.data),
    placeholderData: keepPreviousData,
  })
}
