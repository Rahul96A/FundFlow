import Autocomplete from '@mui/material/Autocomplete'
import Box from '@mui/material/Box'
import Chip from '@mui/material/Chip'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { humanizeRole } from '@/shared/utils/format'
import type { RoleSummary } from '../types/role.types'

interface RolePickerProps {
  roles: RoleSummary[]
  value: string[]
  onChange: (roleIds: string[]) => void
  label?: string
  error?: string
  disabled?: boolean
  loading?: boolean
  autoFocus?: boolean
}

/** Multi-select of the organization's roles, with each role's purpose shown in the dropdown. */
export function RolePicker({ roles, value, onChange, label = 'Roles', error, disabled, loading, autoFocus }: RolePickerProps) {
  const selected = roles.filter((role) => value.includes(role.id))

  return (
    <Autocomplete
      multiple
      disableCloseOnSelect
      options={roles}
      value={selected}
      loading={loading}
      disabled={disabled}
      onChange={(_event, next) => onChange(next.map((role) => role.id))}
      getOptionLabel={(role) => (role.isSystem ? humanizeRole(role.name) : role.name)}
      isOptionEqualToValue={(a, b) => a.id === b.id}
      groupBy={(role) => (role.isSystem ? 'Standard roles' : 'Custom roles')}
      renderOption={({ key, ...props }, role) => (
        <Box component="li" key={key} {...props}>
          <Box sx={{ minWidth: 0 }}>
            <Typography variant="body2" sx={{ fontWeight: 600 }}>
              {role.isSystem ? humanizeRole(role.name) : role.name}
            </Typography>
            {role.description ? (
              <Typography variant="caption" color="textSecondary" component="p">
                {role.description}
              </Typography>
            ) : null}
          </Box>
        </Box>
      )}
      renderValue={(selectedRoles, getItemProps) =>
        selectedRoles.map((role, index) => {
          const { key, ...itemProps } = getItemProps({ index })
          return (
            <Chip
              key={key}
              size="small"
              label={role.isSystem ? humanizeRole(role.name) : role.name}
              {...itemProps}
            />
          )
        })
      }
      renderInput={(params) => (
        <TextField {...params} label={label} error={Boolean(error)} helperText={error} autoFocus={autoFocus} />
      )}
    />
  )
}
