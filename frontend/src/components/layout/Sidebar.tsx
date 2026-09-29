import ChevronLeftIcon from '@mui/icons-material/ChevronLeft'
import ChevronRightIcon from '@mui/icons-material/ChevronRight'
import ExpandLess from '@mui/icons-material/ExpandLess'
import ExpandMore from '@mui/icons-material/ExpandMore'
import Box from '@mui/material/Box'
import Chip from '@mui/material/Chip'
import Collapse from '@mui/material/Collapse'
import Divider from '@mui/material/Divider'
import Drawer from '@mui/material/Drawer'
import IconButton from '@mui/material/IconButton'
import List from '@mui/material/List'
import ListItemButton from '@mui/material/ListItemButton'
import ListItemIcon from '@mui/material/ListItemIcon'
import ListItemText from '@mui/material/ListItemText'
import Tooltip from '@mui/material/Tooltip'
import Typography from '@mui/material/Typography'
import { useTheme } from '@mui/material/styles'
import useMediaQuery from '@mui/material/useMediaQuery'
import { useMemo, useState } from 'react'
import { Link as RouterLink, useLocation } from 'react-router'
import { useAuthStore } from '@/app/store/authStore'
import { useUiStore } from '@/app/store/uiStore'
import { Logo } from '@/components/ui/Logo'
import { visibleNavigation, type NavGroup, type NavItem } from './navigation'

export const SIDEBAR_WIDTH = 272
export const SIDEBAR_COLLAPSED_WIDTH = 76

function isActive(pathname: string, to: string | undefined): boolean {
  return to !== undefined && (pathname === to || pathname.startsWith(`${to}/`))
}

interface ItemProps {
  item: NavItem
  collapsed: boolean
  onNavigate: () => void
}

function NavEntry({ item, collapsed, onNavigate }: ItemProps) {
  const { pathname } = useLocation()
  const active = isActive(pathname, item.to)
  const disabled = item.soon === true || item.to === undefined

  const button = (
    <ListItemButton
      component={disabled ? 'div' : RouterLink}
      {...(disabled ? {} : { to: item.to, onClick: onNavigate })}
      selected={active}
      disabled={disabled}
      aria-current={active ? 'page' : undefined}
      aria-disabled={disabled || undefined}
      sx={{
        borderRadius: 2,
        mx: 1,
        mb: 0.25,
        minHeight: 40,
        justifyContent: collapsed ? 'center' : 'flex-start',
        px: collapsed ? 1 : 1.5,
        '&.Mui-selected': { bgcolor: 'action.selected', color: 'primary.main', '& .MuiListItemIcon-root': { color: 'primary.main' } },
      }}
    >
      <ListItemIcon sx={{ minWidth: collapsed ? 0 : 36, color: 'inherit', justifyContent: 'center' }}>{item.icon}</ListItemIcon>
      {collapsed ? null : (
        <>
          <ListItemText primary={item.label} slotProps={{ primary: { variant: 'body2', noWrap: true, sx: { fontWeight: active ? 650 : 500 } } }} />
          {item.soon ? <Chip label="Soon" size="small" variant="outlined" sx={{ height: 20, fontSize: 11 }} /> : null}
        </>
      )}
    </ListItemButton>
  )

  const title = collapsed ? `${item.label}${item.soon ? ' (coming soon)' : ''}` : item.soon ? 'Coming in a later release' : ''
  return title ? (
    <Tooltip title={title} placement="right">
      {/* A span keeps the tooltip working over a disabled button. */}
      <span>{button}</span>
    </Tooltip>
  ) : (
    button
  )
}

function NavSection({ group, collapsed, onNavigate }: { group: NavGroup; collapsed: boolean; onNavigate: () => void }) {
  const { pathname } = useLocation()
  const hasActive = group.items.some((item) => isActive(pathname, item.to))
  const allSoon = group.items.every((item) => item.soon)
  const [open, setOpen] = useState(hasActive || !allSoon)
  const expanded = collapsed || !group.label || open

  return (
    <Box component="li" sx={{ listStyle: 'none' }}>
      {group.label && !collapsed ? (
        <ListItemButton
          onClick={() => setOpen((v) => !v)}
          aria-expanded={expanded}
          sx={{ mx: 1, borderRadius: 2, minHeight: 32, mt: 1 }}
        >
          <Typography variant="overline" color="textSecondary" sx={{ flex: 1, lineHeight: 2 }}>
            {group.label}
          </Typography>
          {open ? <ExpandLess fontSize="small" color="disabled" /> : <ExpandMore fontSize="small" color="disabled" />}
        </ListItemButton>
      ) : group.label ? (
        <Divider sx={{ my: 1, mx: 2 }} />
      ) : null}
      <Collapse in={expanded} timeout="auto" unmountOnExit>
        <List disablePadding aria-label={group.label}>
          {group.items.map((item) => (
            <NavEntry key={item.label} item={item} collapsed={collapsed} onNavigate={onNavigate} />
          ))}
        </List>
      </Collapse>
    </Box>
  )
}

/** Responsive navigation: a permanent, collapsible rail on desktop and a slide-over drawer on phones and tablets. */
export function Sidebar() {
  const theme = useTheme()
  const isDesktop = useMediaQuery(theme.breakpoints.up('md'))
  const collapsedPref = useUiStore((s) => s.sidebarCollapsed)
  const toggleSidebar = useUiStore((s) => s.toggleSidebar)
  const mobileOpen = useUiStore((s) => s.mobileNavOpen)
  const setMobileOpen = useUiStore((s) => s.setMobileNavOpen)
  const user = useAuthStore((s) => s.user)

  const groups = useMemo(
    () => visibleNavigation({ isPlatformUser: user?.isPlatformUser ?? false, permissions: user?.permissions ?? [] }),
    [user],
  )

  const collapsed = isDesktop && collapsedPref
  const width = collapsed ? SIDEBAR_COLLAPSED_WIDTH : SIDEBAR_WIDTH

  const content = (
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      <Box sx={{ height: 64, display: 'flex', alignItems: 'center', px: collapsed ? 0 : 2.5, justifyContent: collapsed ? 'center' : 'flex-start' }}>
        <Logo compact={collapsed} />
      </Box>
      <Divider />
      <Box component="nav" aria-label="Main navigation" sx={{ flex: 1, overflowY: 'auto', py: 1 }}>
        <List disablePadding>
          {groups.map((group) => (
            <NavSection key={group.id} group={group} collapsed={collapsed} onNavigate={() => setMobileOpen(false)} />
          ))}
        </List>
      </Box>
      {isDesktop ? (
        <>
          <Divider />
          <Box sx={{ p: 1, display: 'flex', justifyContent: collapsed ? 'center' : 'flex-end' }}>
            <Tooltip title={collapsed ? 'Expand navigation' : 'Collapse navigation'} placement="right">
              <IconButton onClick={toggleSidebar} aria-label={collapsed ? 'Expand navigation' : 'Collapse navigation'} size="small">
                {collapsed ? <ChevronRightIcon /> : <ChevronLeftIcon />}
              </IconButton>
            </Tooltip>
          </Box>
        </>
      ) : null}
    </Box>
  )

  if (!isDesktop) {
    return (
      <Drawer
        variant="temporary"
        open={mobileOpen}
        onClose={() => setMobileOpen(false)}
        ModalProps={{ keepMounted: true }}
        slotProps={{ paper: { sx: { width: SIDEBAR_WIDTH }, 'aria-label': 'Navigation' } }}
      >
        {content}
      </Drawer>
    )
  }

  return (
    <Drawer
      variant="permanent"
      sx={{
        width,
        flexShrink: 0,
        transition: (t) => t.transitions.create('width', { duration: t.transitions.duration.shorter }),
        '& .MuiDrawer-paper': {
          width,
          boxSizing: 'border-box',
          borderRight: 1,
          borderColor: 'divider',
          overflowX: 'hidden',
          transition: (t) => t.transitions.create('width', { duration: t.transitions.duration.shorter }),
        },
      }}
    >
      {content}
    </Drawer>
  )
}
