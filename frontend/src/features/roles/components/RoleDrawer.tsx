import DeleteOutlinedIcon from '@mui/icons-material/DeleteOutlined'
import ExpandMoreIcon from '@mui/icons-material/ExpandMore'
import Accordion from '@mui/material/Accordion'
import AccordionDetails from '@mui/material/AccordionDetails'
import AccordionSummary from '@mui/material/AccordionSummary'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Checkbox from '@mui/material/Checkbox'
import Chip from '@mui/material/Chip'
import FormControlLabel from '@mui/material/FormControlLabel'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { useMemo, useState } from 'react'
import { useAuthStore } from '@/app/store/authStore'
import { useConfirm } from '@/components/ui/confirmStore'
import { Drawer } from '@/components/ui/Drawer'
import { ErrorState, LoadingState } from '@/components/ui/States'
import { toast } from '@/components/ui/toastStore'
import { toApiError } from '@/shared/api/problem'
import { humanizeRole } from '@/shared/utils/format'
import { useCreateRole, useDeleteRole, usePermissionCatalog, useRole, useUpdateRole } from '../hooks/useRoles'

export const NEW_ROLE = 'new' as const

interface RoleDrawerProps {
  /** A role id, `NEW_ROLE` to create one, or null when closed. */
  roleId: string | typeof NEW_ROLE | null
  onClose: () => void
  canManage: boolean
}

const ADMIN_ROLES = ['ORGANIZATION_ADMIN', 'SUPER_ADMIN']
const BLANK_ROLE = { id: NEW_ROLE, name: '', description: null, permissions: [] as string[] }

/** View a role's permissions, or create/edit a custom one. System roles are shown read-only. */
export function RoleDrawer({ roleId, onClose, canManage }: RoleDrawerProps) {
  const creating = roleId === NEW_ROLE
  const open = roleId !== null
  const role = useRole(open && !creating ? roleId : null)
  const catalog = usePermissionCatalog(open)
  const create = useCreateRole()
  const update = useUpdateRole()
  const remove = useDeleteRole()
  const confirm = useConfirm()
  const me = useAuthStore((s) => s.user)

  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [banner, setBanner] = useState<string | null>(null)
  const [nameError, setNameError] = useState<string | null>(null)

  // Start over whenever a different role is opened or the server's copy of it changes. Adjusting state while rendering
  // (rather than in an effect) avoids a second render pass and keeps the drawer's own content during its exit animation.
  const initial = creating ? BLANK_ROLE : role.data
  const source = initial ? `${initial.id}|${initial.name}|${initial.description ?? ''}|${initial.permissions.join(',')}` : null
  const [loadedFrom, setLoadedFrom] = useState<string | null>(null)
  if (source !== loadedFrom) {
    setLoadedFrom(source)
    if (initial) {
      setName(initial.name)
      setDescription(initial.description ?? '')
      setSelected(new Set(initial.permissions))
      setBanner(null)
      setNameError(null)
    }
  }

  const readOnly = !canManage || (!creating && role.data?.isSystem === true)
  const isAdmin = me?.roles.some((r) => ADMIN_ROLES.includes(r)) ?? false
  const held = useMemo(() => new Set(me?.permissions ?? []), [me])
  const saving = create.isPending || update.isPending

  // Mirrors the server rule: you can only switch ON permissions you hold yourself (administrators excepted).
  const canGrant = (permission: string) => isAdmin || held.has(permission) || selected.has(permission)

  function toggle(permission: string, checked: boolean) {
    setSelected((current) => {
      const next = new Set(current)
      if (checked) {
        next.add(permission)
      } else {
        next.delete(permission)
      }
      return next
    })
  }

  function save() {
    if (name.trim() === '') {
      setNameError('Enter a role name.')
      return
    }
    setNameError(null)
    setBanner(null)
    const input = { name: name.trim(), description: description.trim() === '' ? null : description.trim(), permissions: [...selected] }
    const onError = (error: unknown) => {
      const apiError = toApiError(error)
      if (apiError.fieldErrors.name?.[0]) {
        setNameError(apiError.fieldErrors.name[0])
      } else {
        setBanner(apiError.message)
      }
    }

    if (creating) {
      create.mutate(input, { onSuccess: () => { toast.success('Role created.'); onClose() }, onError })
    } else if (roleId) {
      update.mutate({ id: roleId, input }, { onSuccess: () => toast.success('Role saved. It applies to users on their next request.'), onError })
    }
  }

  async function handleDelete() {
    if (!role.data) {
      return
    }
    const { confirmed } = await confirm({
      title: `Delete “${role.data.name}”?`,
      message: 'This cannot be undone. Nobody currently holds this role.',
      confirmLabel: 'Delete role',
      destructive: true,
    })
    if (confirmed) {
      remove.mutate(role.data.id, { onSuccess: () => { toast.success('Role deleted.'); onClose() } })
    }
  }

  const title = creating ? 'New role' : role.data ? (role.data.isSystem ? humanizeRole(role.data.name) : role.data.name) : 'Role'

  return (
    <Drawer
      open={open}
      onClose={onClose}
      title={title}
      subtitle={role.data?.isSystem ? 'Standard role · read-only' : creating ? 'Choose exactly what this role can do' : undefined}
      width={560}
      busy={saving}
      footer={
        readOnly ? (
          <Button onClick={onClose}>Close</Button>
        ) : (
          <>
            {!creating && role.data ? (
              <Button
                color="error"
                startIcon={<DeleteOutlinedIcon />}
                onClick={() => void handleDelete()}
                disabled={role.data.userCount > 0 || remove.isPending}
                sx={{ mr: 'auto' }}
                title={role.data.userCount > 0 ? 'Reassign the users who hold this role before deleting it.' : undefined}
              >
                Delete
              </Button>
            ) : null}
            <Button onClick={onClose} color="inherit" disabled={saving}>Cancel</Button>
            <Button onClick={save} variant="contained" disabled={saving}>
              {saving ? 'Saving…' : creating ? 'Create role' : 'Save changes'}
            </Button>
          </>
        )
      }
    >
      {!creating && role.isPending ? (
        <LoadingState variant="skeleton" lines={6} label="Loading role" />
      ) : !creating && role.isError ? (
        <ErrorState error={role.error} onRetry={() => void role.refetch()} />
      ) : (
        <Stack spacing={2.5}>
          {banner ? <Alert severity="error" role="alert">{banner}</Alert> : null}
          {role.data?.isSystem ? (
            <Alert severity="info">
              Standard roles are managed by FundFlow so they stay consistent and safe. To change what a group of people can do,
              create a custom role.
            </Alert>
          ) : null}
          <TextField label="Name" value={role.data?.isSystem ? humanizeRole(name) : name} onChange={(e) => setName(e.target.value)} disabled={readOnly} required error={Boolean(nameError)} helperText={nameError} slotProps={{ htmlInput: { maxLength: 100 } }} />
          <TextField label="Description" value={description} onChange={(e) => setDescription(e.target.value)} disabled={readOnly} multiline minRows={2} slotProps={{ htmlInput: { maxLength: 500 } }} />

          <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center' }}>
            <Typography variant="h5" component="h3">Permissions</Typography>
            <Chip size="small" label={`${selected.size} selected`} />
          </Stack>
          {!readOnly && !isAdmin ? (
            <Typography variant="caption" color="textSecondary">
              You can only turn on permissions you hold yourself.
            </Typography>
          ) : null}

          {catalog.isPending ? (
            <LoadingState variant="skeleton" lines={5} label="Loading permissions" />
          ) : catalog.isError ? (
            <ErrorState error={catalog.error} onRetry={() => void catalog.refetch()} />
          ) : (
            <Box>
              {catalog.data.map((group) => {
                const names = group.permissions.map((p) => p.name)
                const count = names.filter((n) => selected.has(n)).length
                const grantable = names.filter(canGrant)
                return (
                  <Accordion key={group.module} disableGutters variant="outlined">
                    <AccordionSummary expandIcon={<ExpandMoreIcon />} aria-controls={`perm-${group.module}`} id={`perm-head-${group.module}`}>
                      <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center', width: '100%', pr: 1 }}>
                        <Typography variant="subtitle2" sx={{ flex: 1 }}>{group.module}</Typography>
                        <Chip size="small" variant={count > 0 ? 'filled' : 'outlined'} color={count > 0 ? 'primary' : 'default'} label={`${count}/${names.length}`} />
                      </Stack>
                    </AccordionSummary>
                    <AccordionDetails id={`perm-${group.module}`}>
                      {!readOnly && grantable.length > 1 ? (
                        <Button
                          size="small"
                          sx={{ mb: 1 }}
                          onClick={() => grantable.forEach((n) => toggle(n, count !== names.length))}
                        >
                          {count === names.length ? 'Clear all' : 'Select all'}
                        </Button>
                      ) : null}
                      <Stack>
                        {group.permissions.map((permission) => (
                          <FormControlLabel
                            key={permission.name}
                            sx={{ alignItems: 'flex-start', mb: 0.5 }}
                            control={
                              <Checkbox
                                size="small"
                                checked={selected.has(permission.name)}
                                disabled={readOnly || !canGrant(permission.name)}
                                onChange={(_e, checked) => toggle(permission.name, checked)}
                              />
                            }
                            label={
                              <Box sx={{ pt: 0.5 }}>
                                <Typography variant="body2" sx={{ fontWeight: 600 }}>{permission.name}</Typography>
                                <Typography variant="caption" color="textSecondary">{permission.description}</Typography>
                              </Box>
                            }
                          />
                        ))}
                      </Stack>
                    </AccordionDetails>
                  </Accordion>
                )
              })}
            </Box>
          )}
        </Stack>
      )}
    </Drawer>
  )
}
