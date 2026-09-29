import { Navigate, Outlet, useLocation } from 'react-router'
import { useAuthStore, useHasAnyPermission } from '@/app/store/authStore'
import { landingPath } from '@/components/layout/navigation'
import { LoadingState } from '@/components/ui/States'
import { ForbiddenPage } from '@/features/errors/pages/ForbiddenPage'
import { paths } from '@/shared/constants/paths'

/** Shown while the first route's code loads, so a cold start is never a blank page. */
export function StartupFallback() {
  return <LoadingState label="Restoring your session" />
}

/** Everything inside requires a signed-in user; anonymous visitors are sent to sign in and returned afterwards. */
export function RequireAuth() {
  const status = useAuthStore((s) => s.status)
  const location = useLocation()

  if (status === 'loading') {
    return <LoadingState label="Restoring your session" />
  }

  if (status === 'anonymous') {
    return <Navigate to={paths.login} replace state={{ from: `${location.pathname}${location.search}` }} />
  }

  return <Outlet />
}

/** Sign-in and sign-up pages: an already signed-in visitor goes to their home instead. */
export function GuestOnly() {
  const status = useAuthStore((s) => s.status)
  const isPlatformUser = useAuthStore((s) => s.user?.isPlatformUser ?? false)

  if (status === 'loading') {
    return <LoadingState label="Loading" />
  }

  if (status === 'authenticated') {
    return <Navigate to={landingPath(isPlatformUser)} replace />
  }

  return <Outlet />
}

/**
 * Renders a 403 page when the user lacks every listed permission. This is a usability layer only:
 * the API enforces the same permissions on every request.
 */
export function RequirePermission({ permissions }: { permissions: readonly string[] }) {
  const allowed = useHasAnyPermission(permissions)
  return allowed ? <Outlet /> : <ForbiddenPage />
}

/** The dashboard belongs to organizations; platform operators have no tenant data and are sent to the registry. */
export function RequireTenantMember() {
  const isPlatformUser = useAuthStore((s) => s.user?.isPlatformUser ?? false)
  return isPlatformUser ? <Navigate to={paths.platformOrganizations} replace /> : <Outlet />
}

/** The platform registry is for operators only. */
export function RequirePlatformUser() {
  const isPlatformUser = useAuthStore((s) => s.user?.isPlatformUser ?? false)
  return isPlatformUser ? <Outlet /> : <ForbiddenPage />
}

export function HomeRedirect() {
  const isPlatformUser = useAuthStore((s) => s.user?.isPlatformUser ?? false)
  return <Navigate to={landingPath(isPlatformUser)} replace />
}
