import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import { isRouteErrorResponse, useRouteError } from 'react-router'
import { ErrorState } from '@/components/ui/States'

/** Last line of defence: a rendering bug or failed chunk load shows a recoverable message, never a blank screen. */
export default function RouteErrorPage() {
  const error = useRouteError()
  const message = isRouteErrorResponse(error)
    ? `${error.status} ${error.statusText}`
    : error instanceof Error
      ? error.message
      : 'An unexpected error occurred.'

  return (
    <Box sx={{ minHeight: '60vh', display: 'grid', placeItems: 'center' }}>
      <ErrorState
        title="Something went wrong on this page"
        error={new Error(message)}
        onRetry={() => window.location.reload()}
      />
      <Button href="/" sx={{ mt: 2 }}>
        Return home
      </Button>
    </Box>
  )
}
