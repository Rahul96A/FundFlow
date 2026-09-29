import NavigateNextIcon from '@mui/icons-material/NavigateNext'
import Breadcrumbs from '@mui/material/Breadcrumbs'
import Link from '@mui/material/Link'
import Typography from '@mui/material/Typography'
import { Link as RouterLink, useMatches } from 'react-router'

export interface RouteHandle {
  /** Label shown in the breadcrumb trail. Routes without one are skipped. */
  crumb?: string
}

/** Builds the trail from the `handle.crumb` of every matched route, so pages never assemble their own. */
export function AppBreadcrumbs() {
  const matches = useMatches()
  const crumbs = matches
    .map((match) => ({ path: match.pathname, label: (match.handle as RouteHandle | undefined)?.crumb }))
    .filter((crumb): crumb is { path: string; label: string } => Boolean(crumb.label))

  if (crumbs.length === 0) {
    return null
  }

  return (
    <Breadcrumbs aria-label="Breadcrumb" separator={<NavigateNextIcon fontSize="small" />} sx={{ minWidth: 0 }}>
      {crumbs.map((crumb, index) =>
        index === crumbs.length - 1 ? (
          <Typography key={crumb.path} color="textPrimary" variant="body2" aria-current="page" noWrap sx={{ fontWeight: 600 }}>
            {crumb.label}
          </Typography>
        ) : (
          <Link key={crumb.path} component={RouterLink} to={crumb.path} underline="hover" color="inherit" variant="body2">
            {crumb.label}
          </Link>
        ),
      )}
    </Breadcrumbs>
  )
}
