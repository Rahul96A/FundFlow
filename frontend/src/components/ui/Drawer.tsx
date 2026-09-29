import CloseIcon from '@mui/icons-material/Close'
import Box from '@mui/material/Box'
import Divider from '@mui/material/Divider'
import MuiDrawer from '@mui/material/Drawer'
import IconButton from '@mui/material/IconButton'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import type { ReactNode } from 'react'

interface DrawerProps {
  open: boolean
  title: string
  subtitle?: string
  onClose: () => void
  children: ReactNode
  /** Sticky footer, typically the Save / Cancel buttons. */
  footer?: ReactNode
  width?: number
  busy?: boolean
}

/** Right-hand side panel for create/edit/detail workflows that should not lose the list behind them. */
export function Drawer({ open, title, subtitle, onClose, children, footer, width = 480, busy = false }: DrawerProps) {
  return (
    <MuiDrawer
      anchor="right"
      open={open}
      onClose={busy ? undefined : onClose}
      slotProps={{ paper: { sx: { width: { xs: '100%', sm: width }, maxWidth: '100%' }, 'aria-label': title } }}
    >
      <Stack sx={{ height: '100%' }}>
        <Stack direction="row" sx={{ alignItems: 'flex-start', justifyContent: 'space-between', p: 2.5, pb: 2 }}>
          <Box sx={{ minWidth: 0 }}>
            <Typography variant="h3" component="h2" noWrap>
              {title}
            </Typography>
            {subtitle ? (
              <Typography variant="body2" color="textSecondary" noWrap>
                {subtitle}
              </Typography>
            ) : null}
          </Box>
          <IconButton aria-label="Close panel" onClick={onClose} disabled={busy} edge="end">
            <CloseIcon />
          </IconButton>
        </Stack>
        <Divider />
        <Box sx={{ flex: 1, overflowY: 'auto', p: 2.5 }}>{children}</Box>
        {footer ? (
          <>
            <Divider />
            <Stack direction="row" spacing={1.5} sx={{ justifyContent: 'flex-end', p: 2 }}>
              {footer}
            </Stack>
          </>
        ) : null}
      </Stack>
    </MuiDrawer>
  )
}
