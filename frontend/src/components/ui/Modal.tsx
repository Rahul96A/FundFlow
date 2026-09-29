import CloseIcon from '@mui/icons-material/Close'
import Dialog from '@mui/material/Dialog'
import DialogActions from '@mui/material/DialogActions'
import DialogContent from '@mui/material/DialogContent'
import DialogTitle from '@mui/material/DialogTitle'
import IconButton from '@mui/material/IconButton'
import type { ReactNode } from 'react'

interface ModalProps {
  open: boolean
  title: string
  onClose: () => void
  children: ReactNode
  actions?: ReactNode
  maxWidth?: 'xs' | 'sm' | 'md' | 'lg'
  /** Blocks dismissal while a save is in flight. */
  busy?: boolean
}

export function Modal({ open, title, onClose, children, actions, maxWidth = 'sm', busy = false }: ModalProps) {
  return (
    <Dialog
      open={open}
      onClose={busy ? undefined : onClose}
      maxWidth={maxWidth}
      fullWidth
      aria-labelledby="modal-title"
    >
      <DialogTitle id="modal-title" sx={{ pr: 7 }}>
        {title}
        <IconButton
          aria-label="Close dialog"
          onClick={onClose}
          disabled={busy}
          sx={{ position: 'absolute', right: 12, top: 12 }}
        >
          <CloseIcon />
        </IconButton>
      </DialogTitle>
      <DialogContent dividers>{children}</DialogContent>
      {actions ? <DialogActions sx={{ px: 3, py: 2 }}>{actions}</DialogActions> : null}
    </Dialog>
  )
}
