import { zodResolver } from '@hookform/resolvers/zod'
import BlockIcon from '@mui/icons-material/Block'
import LockOpenIcon from '@mui/icons-material/LockOpen'
import MailIcon from '@mui/icons-material/Mail'
import RestoreIcon from '@mui/icons-material/Restore'
import Alert from '@mui/material/Alert'
import Avatar from '@mui/material/Avatar'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Divider from '@mui/material/Divider'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useAuthStore } from '@/app/store/authStore'
import { FormTextField } from '@/components/forms/FormFields'
import { useConfirm } from '@/components/ui/confirmStore'
import { Drawer } from '@/components/ui/Drawer'
import { ErrorState, LoadingState } from '@/components/ui/States'
import { toast } from '@/components/ui/toastStore'
import { RolePicker } from '@/features/roles/components/RolePicker'
import { useRoles } from '@/features/roles/hooks/useRoles'
import { applyFieldErrors, toApiError } from '@/shared/api/problem'
import { formatDateTime, initials } from '@/shared/utils/format'
import { useSetUserRoles, useUpdateUser, useUser, useUserAction } from '../hooks/useUsers'
import { editUserSchema, type EditUserForm } from '../schemas/userSchemas'
import type { UserDetail } from '../types/user.types'
import { UserStatusBadge } from './UserStatusBadge'

interface UserDrawerProps {
  userId: string | null
  onClose: () => void
  canManage: boolean
  canReadRoles: boolean
}

function ProfileSection({ user, canManage }: { user: UserDetail; canManage: boolean }) {
  const update = useUpdateUser()
  const [banner, setBanner] = useState<string | null>(null)
  const { control, handleSubmit, reset, setError, formState } = useForm<EditUserForm>({
    resolver: zodResolver(editUserSchema),
    defaultValues: { firstName: user.firstName, lastName: user.lastName, phoneNumber: user.phoneNumber ?? '' },
  })

  useEffect(
    () => reset({ firstName: user.firstName, lastName: user.lastName, phoneNumber: user.phoneNumber ?? '' }),
    [user, reset],
  )

  const onSubmit = handleSubmit((values) => {
    setBanner(null)
    update.mutate(
      {
        id: user.id,
        input: { firstName: values.firstName, lastName: values.lastName, phoneNumber: values.phoneNumber.trim() === '' ? null : values.phoneNumber.trim() },
      },
      {
        onSuccess: () => toast.success('Profile saved.'),
        onError: (error) => {
          const unmatched = applyFieldErrors(error, ['firstName', 'lastName', 'phoneNumber'] as const, setError)
          setBanner(unmatched[0] ?? (toApiError(error).isValidation ? null : toApiError(error).message))
        },
      },
    )
  })

  return (
    <form onSubmit={onSubmit} noValidate aria-label="User profile">
      <Stack spacing={2}>
        <Typography variant="h5" component="h3">Profile</Typography>
        {banner ? <Alert severity="error" role="alert">{banner}</Alert> : null}
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
          <FormTextField control={control} name="firstName" label="First name" disabled={!canManage} />
          <FormTextField control={control} name="lastName" label="Last name" disabled={!canManage} />
        </Stack>
        <FormTextField control={control} name="phoneNumber" label="Phone" type="tel" disabled={!canManage} />
        {canManage ? (
          <Box>
            <Button type="submit" variant="outlined" disabled={!formState.isDirty || update.isPending}>
              {update.isPending ? 'Saving…' : 'Save profile'}
            </Button>
          </Box>
        ) : null}
      </Stack>
    </form>
  )
}

function RolesSection({ user, canManage, canReadRoles }: { user: UserDetail; canManage: boolean; canReadRoles: boolean }) {
  const roles = useRoles(canReadRoles)
  const setRoles = useSetUserRoles()
  const [selected, setSelected] = useState<string[]>(user.roles.map((r) => r.id))
  const [banner, setBanner] = useState<string | null>(null)
  const original = user.roles.map((r) => r.id)
  const dirty = selected.length !== original.length || selected.some((id) => !original.includes(id))

  return (
    <Stack spacing={2}>
      <Typography variant="h5" component="h3">Roles</Typography>
      {banner ? <Alert severity="error" role="alert">{banner}</Alert> : null}
      {canReadRoles ? (
        <RolePicker
          roles={roles.data ?? []}
          loading={roles.isPending}
          value={selected}
          onChange={setSelected}
          disabled={!canManage}
          error={selected.length === 0 ? 'Choose at least one role.' : undefined}
        />
      ) : (
        <Typography variant="body2" color="textSecondary">
          {user.roles.map((r) => r.name).join(', ')}
        </Typography>
      )}
      {canManage && canReadRoles ? (
        <Box>
          <Button
            variant="outlined"
            disabled={!dirty || selected.length === 0 || setRoles.isPending}
            onClick={() => {
              setBanner(null)
              setRoles.mutate(
                { id: user.id, roleIds: selected },
                {
                  onSuccess: () => toast.success('Roles updated. The change applies to their next request.'),
                  onError: (error) => setBanner(toApiError(error).message),
                },
              )
            }}
          >
            {setRoles.isPending ? 'Saving…' : 'Save roles'}
          </Button>
        </Box>
      ) : null}
    </Stack>
  )
}

function AccountSection({ user, canManage }: { user: UserDetail; canManage: boolean }) {
  const action = useUserAction()
  const confirm = useConfirm()
  const currentUserId = useAuthStore((s) => s.user?.id)
  const timeZone = useAuthStore((s) => s.user?.organization?.timeZoneId)
  const isSelf = currentUserId === user.id

  async function run(kind: 'deactivate' | 'reactivate' | 'unlock' | 'resendInvitation') {
    if (kind === 'deactivate') {
      const { confirmed } = await confirm({
        title: `Deactivate ${user.fullName}?`,
        message: 'They will be signed out everywhere immediately and cannot sign in until you reactivate them.',
        confirmLabel: 'Deactivate',
        destructive: true,
      })
      if (!confirmed) {
        return
      }
    }
    action.mutate(
      { id: user.id, action: kind },
      {
        onSuccess: () =>
          toast.success(
            {
              deactivate: 'User deactivated.',
              reactivate: 'User reactivated.',
              unlock: 'User unlocked.',
              resendInvitation: 'Invitation sent again.',
            }[kind],
          ),
      },
    )
  }

  return (
    <Stack spacing={2}>
      <Typography variant="h5" component="h3">Account</Typography>
      <Stack spacing={0.5}>
        <Typography variant="body2" color="textSecondary">Last sign-in: {formatDateTime(user.lastLoginAt, timeZone)}</Typography>
        <Typography variant="body2" color="textSecondary">Created: {formatDateTime(user.createdAt, timeZone)}</Typography>
        {user.status === 'Locked' && user.lockoutEnd ? (
          <Typography variant="body2" color="error">Locked until {formatDateTime(user.lockoutEnd, timeZone)}</Typography>
        ) : null}
      </Stack>
      {canManage ? (
        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
          {user.status === 'Invited' ? (
            <Button startIcon={<MailIcon />} variant="outlined" onClick={() => void run('resendInvitation')} disabled={action.isPending}>
              Resend invitation
            </Button>
          ) : null}
          {user.status === 'Locked' ? (
            <Button startIcon={<LockOpenIcon />} variant="outlined" onClick={() => void run('unlock')} disabled={action.isPending}>
              Unlock
            </Button>
          ) : null}
          {user.isActive ? (
            <Button
              startIcon={<BlockIcon />}
              variant="outlined"
              color="error"
              onClick={() => void run('deactivate')}
              disabled={action.isPending || isSelf}
              title={isSelf ? 'You cannot deactivate your own account.' : undefined}
            >
              Deactivate
            </Button>
          ) : (
            <Button startIcon={<RestoreIcon />} variant="outlined" onClick={() => void run('reactivate')} disabled={action.isPending}>
              Reactivate
            </Button>
          )}
        </Stack>
      ) : null}
    </Stack>
  )
}

/** Side panel to inspect and manage one user: profile, roles and account state. */
export function UserDrawer({ userId, onClose, canManage, canReadRoles }: UserDrawerProps) {
  const user = useUser(userId)

  return (
    <Drawer open={userId !== null} onClose={onClose} title={user.data?.fullName ?? 'User'} subtitle={user.data?.email} width={520}>
      {user.isPending ? (
        <LoadingState variant="skeleton" lines={6} label="Loading user" />
      ) : user.isError ? (
        <ErrorState error={user.error} onRetry={() => void user.refetch()} />
      ) : (
        <Stack spacing={3} divider={<Divider flexItem />}>
          <Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}>
            <Avatar sx={{ width: 56, height: 56, bgcolor: 'primary.main' }}>{initials(user.data.fullName)}</Avatar>
            <Box>
              <Typography variant="h4">{user.data.fullName}</Typography>
              <UserStatusBadge status={user.data.status} />
            </Box>
          </Stack>
          <ProfileSection user={user.data} canManage={canManage} />
          <RolesSection
            key={`${user.data.id}:${user.data.roles.map((r) => r.id).join(',')}`}
            user={user.data}
            canManage={canManage}
            canReadRoles={canReadRoles}
          />
          <AccountSection user={user.data} canManage={canManage} />
        </Stack>
      )}
    </Drawer>
  )
}
