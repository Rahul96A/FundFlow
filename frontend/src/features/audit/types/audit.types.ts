import type { ListParams } from '@/shared/types/api'

export interface AuditLogEntry {
  id: string
  timestamp: string
  userId: string | null
  userEmail: string | null
  action: string
  entityType: string
  entityId: string | null
  ipAddress: string | null
  userAgent: string | null
  oldValues: Record<string, unknown> | null
  newValues: Record<string, unknown> | null
  correlationId: string | null
}

export interface AuditListParams extends ListParams {
  from?: string
  to?: string
  action?: string
  entityType?: string
  userId?: string
}
