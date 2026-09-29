import { zodResolver } from '@hookform/resolvers/zod'
import Alert from '@mui/material/Alert'
import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { FormTextField } from '@/components/forms/FormFields'
import { Drawer } from '@/components/ui/Drawer'
import { toast } from '@/components/ui/toastStore'
import { RolePicker } from '@/features/roles/components/RolePicker'
import { useRoles } from '@/features/roles/hooks/useRoles'
import { applyFieldErrors, toApiError } from '@/shared/api/problem'
import { useInviteUser } from '../hooks/useUsers'
import { inviteUserSchema, type InviteUserForm } from '../schemas/userSchemas'

const FIELDS = ['email', 'firstName', 'lastName', 'phoneNumber', 'roleIds'] as const

interface InviteUserDrawerProps {
  open: boolean
  onClose: () => void
}

export function InviteUserDrawer({ open, onClose }: InviteUserDrawerProps) {
  const roles = useRoles(open)
  const invite = useInviteUser()
  const [banner, setBanner] = useState<string | null>(null)
  const { control, handleSubmit, reset, setError } = useForm<InviteUserForm>({
    resolver: zodResolver(inviteUserSchema),
    defaultValues: { email: '', firstName: '', lastName: '', phoneNumber: '', roleIds: [] },
  })

  function close() {
    reset()
    setBanner(null)
    onClose()
  }

  const onSubmit = handleSubmit((values) => {
    setBanner(null)
    invite.mutate(
      {
        email: values.email,
        firstName: values.firstName,
        lastName: values.lastName,
        phoneNumber: values.phoneNumber.trim() === '' ? null : values.phoneNumber.trim(),
        roleIds: values.roleIds,
      },
      {
        onSuccess: (user) => {
          toast.success(`Invitation sent to ${user.email}.`)
          close()
        },
        onError: (error) => {
          const unmatched = applyFieldErrors(error, FIELDS, setError)
          const apiError = toApiError(error)
          setBanner(unmatched[0] ?? (apiError.isValidation ? null : apiError.message))
        },
      },
    )
  })

  return (
    <Drawer
      open={open}
      onClose={close}
      title="Invite a user"
      subtitle="They will get an email to set their password."
      busy={invite.isPending}
      footer={
        <>
          <Button onClick={close} color="inherit" disabled={invite.isPending}>
            Cancel
          </Button>
          <Button type="submit" form="invite-user-form" variant="contained" disabled={invite.isPending}>
            {invite.isPending ? 'Sending…' : 'Send invitation'}
          </Button>
        </>
      }
    >
      <form id="invite-user-form" onSubmit={onSubmit} noValidate>
        <Stack spacing={2.5}>
          {banner ? <Alert severity="error" role="alert">{banner}</Alert> : null}
          <FormTextField control={control} name="email" label="Email address" type="email" autoComplete="off" autoFocus required />
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <FormTextField control={control} name="firstName" label="First name" autoComplete="off" required />
            <FormTextField control={control} name="lastName" label="Last name" autoComplete="off" required />
          </Stack>
          <FormTextField control={control} name="phoneNumber" label="Phone (optional)" type="tel" autoComplete="off" />
          <Controller
            control={control}
            name="roleIds"
            render={({ field, fieldState }) => (
              <RolePicker
                roles={roles.data ?? []}
                loading={roles.isPending}
                value={field.value}
                onChange={field.onChange}
                error={fieldState.error?.message}
              />
            )}
          />
          {roles.isError ? (
            <Alert severity="error">Roles could not be loaded: {toApiError(roles.error).message}</Alert>
          ) : (
            <Typography variant="caption" color="textSecondary">
              You can only grant roles whose permissions you hold yourself, unless you are an administrator.
            </Typography>
          )}
        </Stack>
      </form>
    </Drawer>
  )
}
