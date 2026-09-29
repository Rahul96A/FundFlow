import Box from '@mui/material/Box'
import Card from '@mui/material/Card'
import CardContent from '@mui/material/CardContent'
import Skeleton from '@mui/material/Skeleton'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import type { ReactNode } from 'react'

interface StatCardProps {
  label: string
  value: ReactNode
  /** Small supporting line under the value, e.g. "2 pending invitations". */
  caption?: ReactNode
  icon?: ReactNode
  loading?: boolean
}

export function StatCard({ label, value, caption, icon, loading = false }: StatCardProps) {
  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Stack direction="row" spacing={2} sx={{ justifyContent: 'space-between', alignItems: 'flex-start' }}>
          <Box sx={{ minWidth: 0 }}>
            <Typography variant="overline" color="textSecondary" component="p">
              {label}
            </Typography>
            {loading ? (
              <Skeleton variant="text" width={72} sx={{ fontSize: '2rem' }} />
            ) : (
              <Typography variant="h2" component="p" sx={{ fontVariantNumeric: 'tabular-nums' }}>
                {value}
              </Typography>
            )}
            {caption && !loading ? (
              <Typography variant="body2" color="textSecondary">
                {caption}
              </Typography>
            ) : null}
          </Box>
          {icon ? (
            <Box
              aria-hidden
              sx={{
                display: 'grid',
                placeItems: 'center',
                width: 40,
                height: 40,
                borderRadius: 2,
                color: 'primary.main',
                bgcolor: 'action.hover',
                flexShrink: 0,
              }}
            >
              {icon}
            </Box>
          ) : null}
        </Stack>
      </CardContent>
    </Card>
  )
}
