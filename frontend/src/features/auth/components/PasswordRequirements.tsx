import CheckCircleIcon from '@mui/icons-material/CheckCircle'
import RadioButtonUncheckedIcon from '@mui/icons-material/RadioButtonUnchecked'
import List from '@mui/material/List'
import ListItem from '@mui/material/ListItem'
import ListItemIcon from '@mui/material/ListItemIcon'
import ListItemText from '@mui/material/ListItemText'
import { passwordChecks } from '../schemas/authSchemas'

/** Live checklist under a "new password" field. Uses icon + text (never colour alone) for each rule. */
export function PasswordRequirements({ password }: { password: string }) {
  const checks = passwordChecks(password)

  return (
    <List dense disablePadding aria-label="Password requirements" sx={{ mt: -0.5 }}>
      {checks.map((check) => (
        <ListItem key={check.id} disableGutters sx={{ py: 0 }}>
          <ListItemIcon sx={{ minWidth: 28 }}>
            {check.ok ? (
              <CheckCircleIcon fontSize="small" color="success" />
            ) : (
              <RadioButtonUncheckedIcon fontSize="small" color="disabled" />
            )}
          </ListItemIcon>
          <ListItemText
            primary={check.label}
            secondary={check.ok ? 'Met' : 'Not met yet'}
            slotProps={{
              primary: { variant: 'caption', color: check.ok ? 'text.primary' : 'text.secondary' },
              secondary: { sx: { position: 'absolute', width: 1, height: 1, overflow: 'hidden', clip: 'rect(0 0 0 0)' } },
            }}
          />
        </ListItem>
      ))}
    </List>
  )
}
