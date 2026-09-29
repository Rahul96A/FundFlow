import DarkModeIcon from '@mui/icons-material/DarkMode'
import LightModeIcon from '@mui/icons-material/LightMode'
import LogoutIcon from '@mui/icons-material/Logout'
import ManageAccountsIcon from '@mui/icons-material/ManageAccounts'
import MenuIcon from '@mui/icons-material/Menu'
import AppBar from '@mui/material/AppBar'
import Avatar from '@mui/material/Avatar'
import Box from '@mui/material/Box'
import Divider from '@mui/material/Divider'
import IconButton from '@mui/material/IconButton'
import ListItemIcon from '@mui/material/ListItemIcon'
import Menu from '@mui/material/Menu'
import MenuItem from '@mui/material/MenuItem'
import Toolbar from '@mui/material/Toolbar'
import Tooltip from '@mui/material/Tooltip'
import Typography from '@mui/material/Typography'
import { useTheme } from '@mui/material/styles'
import useMediaQuery from '@mui/material/useMediaQuery'
import { useState } from 'react'
import { Link as RouterLink, useNavigate } from 'react-router'
import { useAuthStore } from '@/app/store/authStore'
import { useUiStore } from '@/app/store/uiStore'
import { useSignOut } from '@/features/auth/hooks/useAuthActions'
import { paths } from '@/shared/constants/paths'
import { initials } from '@/shared/utils/format'
import { AppBreadcrumbs } from './AppBreadcrumbs'

export function TopNavigation() {
  const theme = useTheme()
  const isDesktop = useMediaQuery(theme.breakpoints.up('md'))
  const user = useAuthStore((s) => s.user)
  const themeMode = useUiStore((s) => s.themeMode)
  const toggleTheme = useUiStore((s) => s.toggleTheme)
  const setMobileNavOpen = useUiStore((s) => s.setMobileNavOpen)
  const signOut = useSignOut()
  const navigate = useNavigate()
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)

  function handleSignOut() {
    setAnchor(null)
    signOut.mutate(undefined, { onSettled: () => navigate(paths.login, { replace: true }) })
  }

  return (
    <AppBar
      position="sticky"
      color="inherit"
      elevation={0}
      sx={{ borderBottom: 1, borderColor: 'divider', bgcolor: 'background.paper', backgroundImage: 'none' }}
    >
      <Toolbar sx={{ gap: 1, minHeight: 64 }}>
        {isDesktop ? null : (
          <IconButton edge="start" aria-label="Open navigation" onClick={() => setMobileNavOpen(true)}>
            <MenuIcon />
          </IconButton>
        )}
        <AppBreadcrumbs />
        <Box sx={{ flex: 1 }} />

        {user?.organization ? (
          <Typography variant="body2" color="textSecondary" noWrap sx={{ display: { xs: 'none', sm: 'block' }, maxWidth: 220 }}>
            {user.organization.name}
          </Typography>
        ) : null}

        <Tooltip title={themeMode === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'}>
          <IconButton aria-label={themeMode === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'} onClick={toggleTheme}>
            {themeMode === 'dark' ? <LightModeIcon /> : <DarkModeIcon />}
          </IconButton>
        </Tooltip>

        <Tooltip title="Account">
          <IconButton
            aria-label="Account menu"
            aria-haspopup="menu"
            aria-expanded={Boolean(anchor)}
            onClick={(event) => setAnchor(event.currentTarget)}
            sx={{ p: 0.5 }}
          >
            <Avatar sx={{ width: 34, height: 34, fontSize: 14, bgcolor: 'primary.main', color: 'primary.contrastText' }}>
              {initials(user?.fullName ?? '')}
            </Avatar>
          </IconButton>
        </Tooltip>
        <Menu
          anchorEl={anchor}
          open={Boolean(anchor)}
          onClose={() => setAnchor(null)}
          anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
          transformOrigin={{ vertical: 'top', horizontal: 'right' }}
          slotProps={{ paper: { sx: { minWidth: 240, mt: 1 } } }}
        >
          <Box sx={{ px: 2, py: 1.25 }}>
            <Typography variant="subtitle2" noWrap>
              {user?.fullName}
            </Typography>
            <Typography variant="caption" color="textSecondary" noWrap component="p">
              {user?.email}
            </Typography>
          </Box>
          <Divider />
          <MenuItem component={RouterLink} to={paths.account} onClick={() => setAnchor(null)}>
            <ListItemIcon>
              <ManageAccountsIcon fontSize="small" />
            </ListItemIcon>
            Account &amp; security
          </MenuItem>
          <MenuItem onClick={handleSignOut} disabled={signOut.isPending}>
            <ListItemIcon>
              <LogoutIcon fontSize="small" />
            </ListItemIcon>
            Sign out
          </MenuItem>
        </Menu>
      </Toolbar>
    </AppBar>
  )
}
