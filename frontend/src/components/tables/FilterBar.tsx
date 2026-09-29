import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import type { ReactNode } from 'react'

interface FilterBarProps {
  children: ReactNode
  /** Shows "Clear filters" when true. */
  canReset?: boolean
  onReset?: () => void
  /** Right-aligned primary actions, e.g. "Invite user". */
  actions?: ReactNode
}

/** Lays out search + filter controls in a wrapping row, with an optional reset and primary actions. */
export function FilterBar({ children, canReset = false, onReset, actions }: FilterBarProps) {
  return (
    <Stack
      direction={{ xs: 'column', md: 'row' }}
      spacing={1.5}
      role="search"
      sx={{ alignItems: { xs: 'stretch', md: 'center' } }}
    >
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} sx={{ flex: 1, flexWrap: 'wrap', rowGap: 1.5 }}>
        {children}
        {canReset ? (
          <Button color="inherit" size="small" onClick={onReset} sx={{ alignSelf: { sm: 'center' } }}>
            Clear filters
          </Button>
        ) : null}
      </Stack>
      {actions}
    </Stack>
  )
}
