import { StatusBadge } from '@/components/ui/StatusBadge'
import type { UserStatus } from '../types/user.types'
import { userStatusLabel, userStatusTone } from '../userStatus'

export function UserStatusBadge({ status }: { status: UserStatus }) {
  return <StatusBadge label={userStatusLabel(status)} tone={userStatusTone(status)} />
}
