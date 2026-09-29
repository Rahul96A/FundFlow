import { useEffect, useMemo } from 'react'
import { createBrowserRouter, RouterProvider } from 'react-router'
import { AppProviders } from '@/app/providers/AppProviders'
import { routes } from '@/app/router/routes'
import { bootstrapSession } from '@/features/auth/hooks/useAuthActions'

let bootstrap: Promise<void> | null = null

/** Runs the silent sign-in exactly once even under React StrictMode's double-invoked effects. */
function bootstrapOnce(): Promise<void> {
  bootstrap ??= bootstrapSession()
  return bootstrap
}

export default function App() {
  // The router is created once; route guards read the auth store, so nothing needs to be recreated on sign-in.
  const router = useMemo(() => createBrowserRouter(routes), [])

  useEffect(() => {
    void bootstrapOnce()
  }, [])

  return (
    <AppProviders>
      <RouterProvider router={router} />
    </AppProviders>
  )
}
