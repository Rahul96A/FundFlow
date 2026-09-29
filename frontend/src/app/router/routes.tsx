import type { ComponentType } from 'react'
import { Navigate, type RouteObject } from 'react-router'
import { AppShell } from '@/components/layout/AppShell'
import RouteErrorPage from '@/features/errors/pages/RouteErrorPage'
import { Permissions } from '@/shared/constants/permissions'
import { GuestOnly, HomeRedirect, RequireAuth, RequirePermission, RequirePlatformUser, RequireTenantMember, StartupFallback } from './guards'

/**
 * Every page is code-split: the sign-in screen does not download the charting library, and the dashboard does not
 * download the audit-log table.
 */
const page = (load: () => Promise<{ default: ComponentType }>) => async () => ({ Component: (await load()).default })

export const routes: RouteObject[] = [
  {
    errorElement: <RouteErrorPage />,
    HydrateFallback: StartupFallback,
    children: [
      {
        // Sign-in and sign-up: a signed-in visitor is redirected away.
        element: <GuestOnly />,
        children: [
          { path: 'login', lazy: page(() => import('@/features/auth/pages/LoginPage')) },
          { path: 'register', lazy: page(() => import('@/features/auth/pages/RegisterPage')) },
          { path: 'forgot-password', lazy: page(() => import('@/features/auth/pages/ForgotPasswordPage')) },
        ],
      },
      // Emailed links work whether or not someone is signed in.
      { path: 'reset-password', lazy: page(() => import('@/features/auth/pages/ResetPasswordPage')) },
      { path: 'verify-email', lazy: page(() => import('@/features/auth/pages/VerifyEmailPage')) },
      { path: 'accept-invitation', lazy: page(() => import('@/features/auth/pages/AcceptInvitationPage')) },
      {
        element: <RequireAuth />,
        children: [
          {
            element: <AppShell />,
            children: [
              { index: true, element: <HomeRedirect /> },
              {
                element: <RequireTenantMember />,
                children: [
                  {
                    path: 'dashboard',
                    handle: { crumb: 'Dashboard' },
                    lazy: page(() => import('@/features/dashboard/pages/DashboardPage')),
                  },
                ],
              },
              {
                path: 'settings',
                handle: { crumb: 'Settings' },
                children: [
                  { index: true, element: <Navigate to="organization" replace /> },
                  {
                    element: <RequireTenantMember />,
                    children: [
                      {
                        path: 'organization',
                        handle: { crumb: 'Organization' },
                        lazy: page(() => import('@/features/organization/pages/OrganizationSettingsPage')),
                      },
                      {
                        element: <RequirePermission permissions={[Permissions.User.Read]} />,
                        children: [
                          {
                            path: 'users',
                            handle: { crumb: 'Users' },
                            lazy: page(() => import('@/features/users/pages/UsersPage')),
                          },
                        ],
                      },
                      {
                        element: <RequirePermission permissions={[Permissions.Role.Read]} />,
                        children: [
                          {
                            path: 'roles',
                            handle: { crumb: 'Roles & permissions' },
                            lazy: page(() => import('@/features/roles/pages/RolesPage')),
                          },
                        ],
                      },
                    ],
                  },
                ],
              },
              {
                element: <RequirePermission permissions={[Permissions.Audit.Read]} />,
                children: [
                  {
                    path: 'audit-logs',
                    handle: { crumb: 'Audit log' },
                    lazy: page(() => import('@/features/audit/pages/AuditLogsPage')),
                  },
                ],
              },
              {
                path: 'account',
                handle: { crumb: 'Account & security' },
                lazy: page(() => import('@/features/account/pages/AccountPage')),
              },
              {
                element: <RequirePlatformUser />,
                children: [
                  {
                    path: 'platform/organizations',
                    handle: { crumb: 'Organizations' },
                    lazy: page(() => import('@/features/platform/pages/PlatformOrganizationsPage')),
                  },
                ],
              },
              { path: '*', lazy: page(() => import('@/features/errors/pages/NotFoundPage')) },
            ],
          },
        ],
      },
      { path: '*', lazy: page(() => import('@/features/errors/pages/NotFoundPage')) },
    ],
  },
]
