import Chip from '@mui/material/Chip'

export type StatusTone = 'success' | 'warning' | 'error' | 'info' | 'neutral'

interface StatusBadgeProps {
  label: string
  tone?: StatusTone
  size?: 'small' | 'medium'
}

const chipColor: Record<StatusTone, 'success' | 'warning' | 'error' | 'info' | 'default'> = {
  success: 'success',
  warning: 'warning',
  error: 'error',
  info: 'info',
  neutral: 'default',
}

/** A coloured pill for a lifecycle state. Always carries text, never colour alone. */
export function StatusBadge({ label, tone = 'neutral', size = 'small' }: StatusBadgeProps) {
  return <Chip label={label} size={size} color={chipColor[tone]} variant={tone === 'neutral' ? 'outlined' : 'filled'} />
}
