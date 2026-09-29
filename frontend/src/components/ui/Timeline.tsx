import Box from '@mui/material/Box'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import type { ReactNode } from 'react'

export interface TimelineEntry {
  id: string
  title: ReactNode
  description?: ReactNode
  /** Already formatted for display. */
  time: string
  icon?: ReactNode
  tone?: 'default' | 'success' | 'warning' | 'error' | 'info'
}

interface TimelineProps {
  entries: TimelineEntry[]
  ariaLabel: string
}

/** Vertical event list with a connecting rail; each entry has an icon dot, a title, detail and a timestamp. */
export function Timeline({ entries, ariaLabel }: TimelineProps) {
  return (
    <Box component="ol" aria-label={ariaLabel} sx={{ listStyle: 'none', m: 0, p: 0 }}>
      {entries.map((entry, index) => (
        <Stack component="li" key={entry.id} direction="row" spacing={1.5} sx={{ position: 'relative', pb: 2 }}>
          <Box sx={{ position: 'relative', width: 28, flexShrink: 0, display: 'flex', justifyContent: 'center' }}>
            {index < entries.length - 1 ? (
              <Box aria-hidden sx={{ position: 'absolute', top: 28, bottom: -16, width: 2, bgcolor: 'divider' }} />
            ) : null}
            <Box
              aria-hidden
              sx={{
                width: 28,
                height: 28,
                borderRadius: '50%',
                display: 'grid',
                placeItems: 'center',
                color: entry.tone && entry.tone !== 'default' ? `${entry.tone}.main` : 'text.secondary',
                bgcolor: 'action.hover',
                '& svg': { fontSize: 16 },
              }}
            >
              {entry.icon}
            </Box>
          </Box>
          <Box sx={{ minWidth: 0, flex: 1 }}>
            <Typography variant="body2" sx={{ fontWeight: 600 }}>
              {entry.title}
            </Typography>
            {entry.description ? (
              <Typography variant="body2" color="textSecondary">
                {entry.description}
              </Typography>
            ) : null}
            <Typography variant="caption" color="textSecondary">
              {entry.time}
            </Typography>
          </Box>
        </Stack>
      ))}
    </Box>
  )
}
