import ErrorOutlinedIcon from '@mui/icons-material/ErrorOutlined'
import InboxOutlinedIcon from '@mui/icons-material/InboxOutlined'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import CircularProgress from '@mui/material/CircularProgress'
import Skeleton from '@mui/material/Skeleton'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import type { ReactNode } from 'react'
import { toApiError } from '@/shared/api/problem'

interface EmptyStateProps {
  title: string
  description?: ReactNode
  icon?: ReactNode
  action?: ReactNode
}

/** Nothing here yet, and here is what to do about it. */
export function EmptyState({ title, description, icon, action }: EmptyStateProps) {
  return (
    <Stack spacing={1.5} sx={{ alignItems: 'center', py: 6, px: 2, textAlign: 'center' }}>
      <Box aria-hidden sx={{ color: 'text.disabled', display: 'grid', placeItems: 'center' }}>
        {icon ?? <InboxOutlinedIcon sx={{ fontSize: 48 }} />}
      </Box>
      <Typography variant="h4" component="h2">
        {title}
      </Typography>
      {description ? (
        <Typography variant="body2" color="textSecondary" sx={{ maxWidth: 420 }}>
          {description}
        </Typography>
      ) : null}
      {action}
    </Stack>
  )
}

interface LoadingStateProps {
  label?: string
  /** `page` centres a spinner in a tall area; `inline` is a compact spinner; `skeleton` draws placeholder lines. */
  variant?: 'page' | 'inline' | 'skeleton'
  lines?: number
}

export function LoadingState({ label = 'Loading', variant = 'page', lines = 4 }: LoadingStateProps) {
  if (variant === 'skeleton') {
    return (
      <Stack spacing={1.5} role="status" aria-label={label} aria-busy="true">
        {Array.from({ length: lines }, (_, i) => (
          <Skeleton key={i} variant="rounded" height={i === 0 ? 28 : 20} width={i === 0 ? '40%' : '100%'} />
        ))}
      </Stack>
    )
  }

  return (
    <Stack
      role="status"
      aria-live="polite"
      spacing={1.5}
      sx={{ alignItems: 'center', justifyContent: 'center', py: variant === 'page' ? 10 : 2 }}
    >
      <CircularProgress size={variant === 'page' ? 36 : 22} aria-label={label} />
      {variant === 'page' ? (
        <Typography variant="body2" color="textSecondary">
          {label}…
        </Typography>
      ) : null}
    </Stack>
  )
}

interface ErrorStateProps {
  error?: unknown
  title?: string
  onRetry?: () => void
}

/** A failed load: says what happened in plain words and offers a retry. Quotes the trace id for support. */
export function ErrorState({ error, title = 'We could not load this', onRetry }: ErrorStateProps) {
  const apiError = error ? toApiError(error) : undefined
  return (
    <Stack spacing={1.5} role="alert" sx={{ alignItems: 'center', py: 6, px: 2, textAlign: 'center' }}>
      <ErrorOutlinedIcon color="error" sx={{ fontSize: 44 }} aria-hidden />
      <Typography variant="h4" component="h2">
        {title}
      </Typography>
      <Typography variant="body2" color="textSecondary" sx={{ maxWidth: 480 }}>
        {apiError?.message ?? 'Something went wrong. Please try again.'}
      </Typography>
      {apiError?.traceId ? (
        <Typography variant="caption" color="textSecondary">
          Reference: {apiError.traceId}
        </Typography>
      ) : null}
      {onRetry ? (
        <Button onClick={onRetry} variant="outlined">
          Try again
        </Button>
      ) : null}
    </Stack>
  )
}
