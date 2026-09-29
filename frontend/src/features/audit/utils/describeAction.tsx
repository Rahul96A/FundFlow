import ApartmentIcon from '@mui/icons-material/Apartment'
import BlockIcon from '@mui/icons-material/Block'
import HistoryIcon from '@mui/icons-material/History'
import KeyIcon from '@mui/icons-material/Key'
import LockOpenIcon from '@mui/icons-material/LockOpen'
import LoginIcon from '@mui/icons-material/Login'
import LogoutIcon from '@mui/icons-material/Logout'
import MailIcon from '@mui/icons-material/Mail'
import PersonAddIcon from '@mui/icons-material/PersonAdd'
import PersonIcon from '@mui/icons-material/Person'
import ShieldIcon from '@mui/icons-material/Shield'
import VerifiedIcon from '@mui/icons-material/Verified'
import WarningAmberIcon from '@mui/icons-material/WarningAmber'
import type { ReactElement } from 'react'
import type { StatusTone } from '@/components/ui/StatusBadge'

interface ActionInfo {
  label: string
  icon: ReactElement
  tone: StatusTone
}

const known: Record<string, ActionInfo> = {
  'Auth.Login': { label: 'Signed in', icon: <LoginIcon />, tone: 'success' },
  'Auth.LoginFailed': { label: 'Failed sign-in attempt', icon: <WarningAmberIcon />, tone: 'warning' },
  'Auth.AccountLocked': { label: 'Account locked after repeated failures', icon: <BlockIcon />, tone: 'error' },
  'Auth.Logout': { label: 'Signed out', icon: <LogoutIcon />, tone: 'neutral' },
  'Auth.TokenReuseDetected': { label: 'Session token reuse detected', icon: <WarningAmberIcon />, tone: 'error' },
  'Auth.SessionRevoked': { label: 'Session signed out', icon: <LogoutIcon />, tone: 'neutral' },
  'Auth.PasswordChanged': { label: 'Password changed', icon: <KeyIcon />, tone: 'info' },
  'Auth.PasswordResetRequested': { label: 'Password reset requested', icon: <MailIcon />, tone: 'info' },
  'Auth.PasswordReset': { label: 'Password reset', icon: <KeyIcon />, tone: 'info' },
  'Auth.EmailVerified': { label: 'Email address verified', icon: <VerifiedIcon />, tone: 'success' },
  'User.Created': { label: 'User created', icon: <PersonAddIcon />, tone: 'info' },
  'User.Invited': { label: 'User invited', icon: <PersonAddIcon />, tone: 'info' },
  'User.InvitationAccepted': { label: 'Invitation accepted', icon: <VerifiedIcon />, tone: 'success' },
  'User.Updated': { label: 'User updated', icon: <PersonIcon />, tone: 'neutral' },
  'User.Deactivated': { label: 'User deactivated', icon: <BlockIcon />, tone: 'warning' },
  'User.Reactivated': { label: 'User reactivated', icon: <PersonIcon />, tone: 'success' },
  'User.Unlocked': { label: 'User unlocked', icon: <LockOpenIcon />, tone: 'info' },
  'User.RolesChanged': { label: 'User roles changed', icon: <ShieldIcon />, tone: 'info' },
  'Role.Created': { label: 'Role created', icon: <ShieldIcon />, tone: 'info' },
  'Role.Updated': { label: 'Role updated', icon: <ShieldIcon />, tone: 'info' },
  'Role.Deleted': { label: 'Role deleted', icon: <ShieldIcon />, tone: 'warning' },
  'Organization.Registered': { label: 'Organization created', icon: <ApartmentIcon />, tone: 'success' },
  'Organization.Updated': { label: 'Organization profile updated', icon: <ApartmentIcon />, tone: 'neutral' },
  'Organization.SettingsUpdated': { label: 'Organization settings updated', icon: <ApartmentIcon />, tone: 'neutral' },
  'Organization.Suspended': { label: 'Organization suspended', icon: <BlockIcon />, tone: 'error' },
  'Organization.Activated': { label: 'Organization reactivated', icon: <ApartmentIcon />, tone: 'success' },
}

/** Human wording, icon and tone for an audit action code. Unknown (future) actions degrade gracefully. */
export function describeAction(action: string): ActionInfo {
  const info = known[action]
  if (info) {
    return info
  }
  const tail = action.split('.').slice(1).join(' ') || action
  const words = tail.replace(/([a-z0-9])([A-Z])/g, '$1 $2').toLowerCase()
  return { label: words.charAt(0).toUpperCase() + words.slice(1), icon: <HistoryIcon />, tone: 'neutral' }
}

/** The filter choices on the audit page: whole modules plus the security events people look for most. */
export const auditActionFilters = [
  { value: '', label: 'All activity' },
  { value: 'Auth.', label: 'Authentication' },
  { value: 'Auth.LoginFailed', label: '– Failed sign-ins' },
  { value: 'User.', label: 'Users' },
  { value: 'Role.', label: 'Roles' },
  { value: 'Organization.', label: 'Organization' },
] as const
