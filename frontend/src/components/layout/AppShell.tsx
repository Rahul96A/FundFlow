import Box from '@mui/material/Box'
import LinearProgress from '@mui/material/LinearProgress'
import Link from '@mui/material/Link'
import { Outlet, useNavigation } from 'react-router'
import { Sidebar } from './Sidebar'
import { TopNavigation } from './TopNavigation'

/** Signed-in layout: navigation rail, top bar, and the routed page. */
export function AppShell() {
  const navigation = useNavigation()

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh', bgcolor: 'background.default' }}>
      <Link
        href="#main-content"
        sx={{
          position: 'absolute',
          left: 8,
          top: -48,
          zIndex: (t) => t.zIndex.tooltip + 1,
          bgcolor: 'background.paper',
          p: 1,
          borderRadius: 1,
          '&:focus': { top: 8 },
        }}
      >
        Skip to main content
      </Link>
      <Sidebar />
      <Box sx={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column' }}>
        <TopNavigation />
        {navigation.state === 'loading' ? <LinearProgress sx={{ position: 'sticky', top: 64, zIndex: 1 }} aria-label="Loading page" /> : null}
        <Box
          component="main"
          id="main-content"
          tabIndex={-1}
          sx={{ flex: 1, width: '100%', maxWidth: 1440, mx: 'auto', p: { xs: 2, md: 3 }, outline: 'none' }}
        >
          <Outlet />
        </Box>
      </Box>
    </Box>
  )
}
