import CheckIcon from '@mui/icons-material/Check'
import Box from '@mui/material/Box'
import List from '@mui/material/List'
import ListItem from '@mui/material/ListItem'
import ListItemIcon from '@mui/material/ListItemIcon'
import ListItemText from '@mui/material/ListItemText'
import Paper from '@mui/material/Paper'
import Typography from '@mui/material/Typography'
import type { ReactNode } from 'react'
import { Logo } from '@/components/ui/Logo'

const highlights = [
  'Donor CRM with giving history, segments and relationships',
  'Campaign pages, one-time and recurring donations',
  'Events, ticketing, check-in and live or silent auctions',
  'Reporting your board and auditors can trust',
]

interface AuthLayoutProps {
  title: string
  subtitle?: ReactNode
  children: ReactNode
  /** Links under the card ("New here? Create an account"). */
  footer?: ReactNode
}

/** Split-screen frame for every public authentication page. The brand panel is hidden on phones. */
export function AuthLayout({ title, subtitle, children, footer }: AuthLayoutProps) {
  return (
    <Box sx={{ minHeight: '100vh', display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'minmax(360px, 5fr) 6fr' }, bgcolor: 'background.default' }}>
      <Box
        sx={{
          display: { xs: 'none', md: 'flex' },
          flexDirection: 'column',
          justifyContent: 'space-between',
          p: 6,
          color: '#fff',
          background: 'linear-gradient(145deg, #0b2a8f 0%, #1f5eff 55%, #4b8bff 100%)',
        }}
      >
        <Logo inverted />
        <Box>
          <Typography variant="h1" component="p" sx={{ color: '#fff', maxWidth: 460, mb: 3 }}>
            Fundraising that flows.
          </Typography>
          <List disablePadding>
            {highlights.map((item) => (
              <ListItem key={item} disableGutters sx={{ alignItems: 'flex-start' }}>
                <ListItemIcon sx={{ minWidth: 32, mt: 0.5, color: '#fff' }}>
                  <CheckIcon fontSize="small" />
                </ListItemIcon>
                <ListItemText primary={item} slotProps={{ primary: { sx: { color: 'rgba(255,255,255,0.92)' } } }} />
              </ListItem>
            ))}
          </List>
        </Box>
        <Typography variant="caption" sx={{ color: 'rgba(255,255,255,0.75)' }}>
          Built for nonprofits that need an enterprise-grade platform without an enterprise price tag.
        </Typography>
      </Box>

      <Box component="main" sx={{ display: 'flex', alignItems: 'center', justifyContent: 'center', p: { xs: 2, sm: 4 } }}>
        <Box sx={{ width: '100%', maxWidth: 460 }}>
          <Box sx={{ display: { xs: 'block', md: 'none' }, mb: 3 }}>
            <Logo />
          </Box>
          <Paper variant="outlined" sx={{ p: { xs: 3, sm: 4 }, borderRadius: 3 }}>
            <Typography variant="h2" component="h1" sx={{ mb: 0.5 }}>
              {title}
            </Typography>
            {subtitle ? (
              <Typography variant="body2" color="textSecondary" sx={{ mb: 3 }}>
                {subtitle}
              </Typography>
            ) : (
              <Box sx={{ mb: 3 }} />
            )}
            {children}
          </Paper>
          {footer ? (
            <Typography variant="body2" color="textSecondary" align="center" sx={{ mt: 3 }}>
              {footer}
            </Typography>
          ) : null}
        </Box>
      </Box>
    </Box>
  )
}
