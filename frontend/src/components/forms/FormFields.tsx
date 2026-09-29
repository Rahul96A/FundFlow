import Visibility from '@mui/icons-material/Visibility'
import VisibilityOff from '@mui/icons-material/VisibilityOff'
import Checkbox from '@mui/material/Checkbox'
import FormControl from '@mui/material/FormControl'
import FormControlLabel from '@mui/material/FormControlLabel'
import FormHelperText from '@mui/material/FormHelperText'
import IconButton from '@mui/material/IconButton'
import InputAdornment from '@mui/material/InputAdornment'
import MenuItem from '@mui/material/MenuItem'
import TextField, { type TextFieldProps } from '@mui/material/TextField'
import { useState, type ReactNode } from 'react'
import { Controller, type Control, type FieldValues, type Path } from 'react-hook-form'

type BaseProps<T extends FieldValues> = Omit<TextFieldProps, 'name' | 'defaultValue' | 'error' | 'value' | 'onChange' | 'onBlur'> & {
  control: Control<T>
  name: Path<T>
  helperText?: ReactNode
}

/** A react-hook-form bound text input that shows its validation message and focuses itself on error. */
export function FormTextField<T extends FieldValues>({ control, name, helperText, ...rest }: BaseProps<T>) {
  return (
    <Controller
      control={control}
      name={name}
      render={({ field: { ref, value, onChange, onBlur }, fieldState }) => (
        <TextField
          {...rest}
          inputRef={ref}
          value={value ?? ''}
          onChange={onChange}
          onBlur={onBlur}
          error={Boolean(fieldState.error)}
          helperText={fieldState.error?.message ?? helperText}
        />
      )}
    />
  )
}

/** Password input with a show/hide toggle. Never autofills a new password with the saved one. */
export function PasswordField<T extends FieldValues>({ control, name, helperText, autoComplete, ...rest }: BaseProps<T>) {
  const [visible, setVisible] = useState(false)

  return (
    <Controller
      control={control}
      name={name}
      render={({ field: { ref, value, onChange, onBlur }, fieldState }) => (
        <TextField
          {...rest}
          inputRef={ref}
          value={value ?? ''}
          onChange={onChange}
          onBlur={onBlur}
          type={visible ? 'text' : 'password'}
          autoComplete={autoComplete ?? 'current-password'}
          error={Boolean(fieldState.error)}
          helperText={fieldState.error?.message ?? helperText}
          slotProps={{
            input: {
              endAdornment: (
                <InputAdornment position="end">
                  <IconButton
                    edge="end"
                    size="small"
                    aria-label={visible ? 'Hide password' : 'Show password'}
                    aria-pressed={visible}
                    onClick={() => setVisible((v) => !v)}
                    onMouseDown={(event) => event.preventDefault()}
                  >
                    {visible ? <VisibilityOff fontSize="small" /> : <Visibility fontSize="small" />}
                  </IconButton>
                </InputAdornment>
              ),
            },
          }}
        />
      )}
    />
  )
}

export interface SelectOption {
  value: string
  label: string
}

type SelectProps<T extends FieldValues> = BaseProps<T> & { options: readonly SelectOption[]; placeholder?: string }

export function FormSelect<T extends FieldValues>({ control, name, options, helperText, placeholder, ...rest }: SelectProps<T>) {
  return (
    <Controller
      control={control}
      name={name}
      render={({ field: { ref, value, onChange, onBlur }, fieldState }) => (
        <TextField
          {...rest}
          select
          inputRef={ref}
          value={value ?? ''}
          onChange={onChange}
          onBlur={onBlur}
          error={Boolean(fieldState.error)}
          helperText={fieldState.error?.message ?? helperText}
        >
          {placeholder ? (
            <MenuItem value="">
              <em>{placeholder}</em>
            </MenuItem>
          ) : null}
          {options.map((option) => (
            <MenuItem key={option.value} value={option.value}>
              {option.label}
            </MenuItem>
          ))}
        </TextField>
      )}
    />
  )
}

interface CheckboxProps<T extends FieldValues> {
  control: Control<T>
  name: Path<T>
  label: ReactNode
  helperText?: ReactNode
}

export function FormCheckbox<T extends FieldValues>({ control, name, label, helperText }: CheckboxProps<T>) {
  return (
    <Controller
      control={control}
      name={name}
      render={({ field: { ref, value, onChange, onBlur }, fieldState }) => (
        <FormControl error={Boolean(fieldState.error)}>
          <FormControlLabel
            control={
              <Checkbox
                checked={Boolean(value)}
                onChange={(_e, checked) => onChange(checked)}
                onBlur={onBlur}
                slotProps={{ input: { ref } }}
              />
            }
            label={label}
          />
          {fieldState.error?.message || helperText ? (
            <FormHelperText>{fieldState.error?.message ?? helperText}</FormHelperText>
          ) : null}
        </FormControl>
      )}
    />
  )
}
