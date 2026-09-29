import Alert from '@mui/material/Alert'
import Snackbar from '@mui/material/Snackbar'
import { useToastStore } from './toastStore'

/** Renders the oldest pending toast; the next one appears when it closes. Mount once near the root. */
export function ToastHost() {
  const toasts = useToastStore((state) => state.toasts)
  const dismiss = useToastStore((state) => state.dismiss)
  const current = toasts[0]

  return (
    <Snackbar
      key={current?.id}
      open={Boolean(current)}
      autoHideDuration={current?.severity === 'error' ? 8000 : 4500}
      onClose={(_event, reason) => {
        if (reason !== 'clickaway' && current) {
          dismiss(current.id)
        }
      }}
      anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
    >
      {current ? (
        <Alert
          onClose={() => dismiss(current.id)}
          severity={current.severity}
          variant="filled"
          role={current.severity === 'error' ? 'alert' : 'status'}
          sx={{ width: '100%', maxWidth: 520 }}
        >
          {current.message}
        </Alert>
      ) : undefined}
    </Snackbar>
  )
}
