import CssBaseline from '@mui/material/CssBaseline'
import { ThemeProvider } from '@mui/material/styles'
import { QueryClientProvider } from '@tanstack/react-query'
import { useMemo, type ReactNode } from 'react'
import { useUiStore } from '@/app/store/uiStore'
import { createAppTheme } from '@/app/theme/theme'
import { ConfirmHost } from '@/components/ui/ConfirmDialog'
import { ToastHost } from '@/components/ui/Toast'
import { queryClient } from '@/shared/api/queryClient'

/** Everything the app needs above the router: server-state cache, theme, and the global toast/confirm hosts. */
export function AppProviders({ children }: { children: ReactNode }) {
  const mode = useUiStore((s) => s.themeMode)
  const theme = useMemo(() => createAppTheme(mode), [mode])

  return (
    <QueryClientProvider client={queryClient}>
      <ThemeProvider theme={theme}>
        <CssBaseline />
        {children}
        <ToastHost />
        <ConfirmHost />
      </ThemeProvider>
    </QueryClientProvider>
  )
}
