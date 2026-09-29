import Button from '@mui/material/Button'
import Dialog from '@mui/material/Dialog'
import DialogActions from '@mui/material/DialogActions'
import DialogContent from '@mui/material/DialogContent'
import DialogContentText from '@mui/material/DialogContentText'
import DialogTitle from '@mui/material/DialogTitle'
import TextField from '@mui/material/TextField'
import { useState, type ReactNode } from 'react'
import { useConfirmStore } from './confirmStore'

export interface ConfirmDialogProps {
  open: boolean
  title: string
  message: ReactNode
  confirmLabel?: string
  cancelLabel?: string
  /** Renders the confirm button in the error colour for irreversible or risky actions. */
  destructive?: boolean
  loading?: boolean
  /** When set, the user must type a reason; it is passed to onConfirm. */
  reasonLabel?: string
  onConfirm: (reason?: string) => void
  onCancel: () => void
}

export function ConfirmDialog({
  open,
  title,
  message,
  confirmLabel = 'Confirm',
  cancelLabel = 'Cancel',
  destructive = false,
  loading = false,
  reasonLabel,
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  const [reason, setReason] = useState('')
  const needsReason = reasonLabel !== undefined
  const canConfirm = !needsReason || reason.trim().length > 0

  return (
    <Dialog
      open={open}
      onClose={loading ? undefined : onCancel}
      aria-labelledby="confirm-title"
      aria-describedby="confirm-message"
      maxWidth="xs"
      fullWidth
      slotProps={{ transition: { onExited: () => setReason('') } }}
    >
      <DialogTitle id="confirm-title">{title}</DialogTitle>
      <DialogContent>
        <DialogContentText id="confirm-message" component="div">
          {message}
        </DialogContentText>
        {needsReason ? (
          <TextField
            autoFocus
            margin="normal"
            label={reasonLabel}
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            multiline
            minRows={2}
            slotProps={{ htmlInput: { maxLength: 500 } }}
          />
        ) : null}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2 }}>
        <Button onClick={onCancel} disabled={loading} color="inherit">
          {cancelLabel}
        </Button>
        <Button
          onClick={() => onConfirm(needsReason ? reason.trim() : undefined)}
          disabled={loading || !canConfirm}
          variant="contained"
          color={destructive ? 'error' : 'primary'}
          autoFocus={!needsReason}
        >
          {confirmLabel}
        </Button>
      </DialogActions>
    </Dialog>
  )
}

/** Mount once near the root; renders whichever imperative confirmation (see useConfirm) is pending. */
export function ConfirmHost() {
  const options = useConfirmStore((state) => state.options)
  const settle = useConfirmStore((state) => state.settle)

  return (
    <ConfirmDialog
      open={options !== null}
      title={options?.title ?? ''}
      message={options?.message}
      confirmLabel={options?.confirmLabel}
      destructive={options?.destructive}
      reasonLabel={options?.reasonLabel}
      onConfirm={(reason) => settle(true, reason)}
      onCancel={() => settle(false)}
    />
  )
}
