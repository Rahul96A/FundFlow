import FormControl from '@mui/material/FormControl'
import FormHelperText from '@mui/material/FormHelperText'
import FormLabel from '@mui/material/FormLabel'
import type { ReactNode } from 'react'

interface FormFieldProps {
  label: string
  /** id of the control inside, so the label is programmatically tied to it. */
  htmlFor?: string
  helperText?: ReactNode
  error?: string
  required?: boolean
  children: ReactNode
}

/** Label, help text and error wrapper for controls that are not plain text inputs (colour pickers, chip lists...). */
export function FormField({ label, htmlFor, helperText, error, required = false, children }: FormFieldProps) {
  return (
    <FormControl fullWidth error={Boolean(error)} required={required}>
      <FormLabel htmlFor={htmlFor} sx={{ mb: 0.75, fontWeight: 600, color: 'text.primary' }}>
        {label}
      </FormLabel>
      {children}
      {error || helperText ? <FormHelperText>{error ?? helperText}</FormHelperText> : null}
    </FormControl>
  )
}
