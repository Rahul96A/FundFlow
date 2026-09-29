import type { StatusTone } from '@/components/ui/StatusBadge'
import type { UserStatus } from './types/user.types'

const presentation: Record<UserStatus, { label: string; tone: StatusTone }> = {
  Active: { label: 'Active', tone: 'success' },
  Invited: { label: 'Invited', tone: 'info' },
  PendingVerification: { label: 'Unverified', tone: 'warning' },
  Locked: { label: 'Locked', tone: 'error' },
  Deactivated: { label: 'Deactivated', tone: 'neutral' },
}

export function userStatusLabel(status: UserStatus): string {
  return presentation[status].label
}

export function userStatusTone(status: UserStatus): StatusTone {
  return presentation[status].tone
}
