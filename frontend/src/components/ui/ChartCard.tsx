import Card from '@mui/material/Card'
import CardContent from '@mui/material/CardContent'
import CardHeader from '@mui/material/CardHeader'
import Skeleton from '@mui/material/Skeleton'
import type { ReactNode } from 'react'
import { EmptyState, ErrorState } from './States'

interface ChartCardProps {
  title: string
  subtitle?: string
  action?: ReactNode
  loading?: boolean
  error?: unknown
  onRetry?: () => void
  /** When true, shows the empty state instead of the chart. */
  empty?: boolean
  emptyTitle?: string
  emptyDescription?: string
  height?: number
  children: ReactNode
}

/** A titled card that hosts a chart and handles its loading, error and empty states uniformly. */
export function ChartCard({
  title,
  subtitle,
  action,
  loading = false,
  error,
  onRetry,
  empty = false,
  emptyTitle = 'No data yet',
  emptyDescription,
  height = 260,
  children,
}: ChartCardProps) {
  return (
    <Card sx={{ height: '100%' }}>
      <CardHeader
        title={title}
        subheader={subtitle}
        action={action}
        slotProps={{ title: { variant: 'h5', component: 'h2' }, subheader: { variant: 'body2' } }}
      />
      <CardContent sx={{ pt: 0 }}>
        {loading ? (
          <Skeleton variant="rounded" height={height} />
        ) : error ? (
          <ErrorState error={error} onRetry={onRetry} title="Could not load this chart" />
        ) : empty ? (
          <EmptyState title={emptyTitle} description={emptyDescription} />
        ) : (
          <div style={{ width: '100%', height }}>{children}</div>
        )}
      </CardContent>
    </Card>
  )
}
