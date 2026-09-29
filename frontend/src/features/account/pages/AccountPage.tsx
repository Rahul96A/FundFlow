import { zodResolver } from '@hookform/resolvers/zod'
import ComputerIcon from '@mui/icons-material/Computer'
import PhoneIphoneIcon from '@mui/icons-material/PhoneIphone'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Card from '@mui/material/Card'
import CardContent from '@mui/material/CardContent'
import CardHeader from '@mui/material/CardHeader'
import Chip from '@mui/material/Chip'
import Grid from '@mui/material/Grid'
import List from '@mui/material/List'
import ListItem from '@mui/material/ListItem'
import ListItemIcon from '@mui/material/ListItemIcon'
import ListItemText from '@mui/material/ListItemText'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useAuthStore } from '@/app/store/authStore'
import { FormTextField, PasswordField } from '@/components/forms/FormFields'
import { useConfirm } from '@/components/ui/confirmStore'
import { PageHeader } from '@/components/ui/PageHeader'
import { ErrorState, LoadingState } from '@/components/ui/States'
import { toast } from '@/components/ui/toastStore'
import { authApi } from '@/features/auth/api/authApi'
import { PasswordRequirements } from '@/features/auth/components/PasswordRequirements'
import { refreshCurrentUser } from '@/features/auth/hooks/useAuthActions'
import {
  changePasswordSchema,
  profileSchema,
  type ChangePasswordForm,
  type ProfileForm,
} from '@/features/auth/schemas/authSchemas'
import { applyFieldErrors, toApiError } from '@/shared/api/problem'
import { describeUserAgent, formatDateTime, humanizeRole, formatRelative } from '@/shared/utils/format'

const sessionsKey = ['auth', 'sessions'] as const

function ProfileCard() {
  const user = useAuthStore((s) => s.user)!
  const [banner, setBanner] = useState<string | null>(null)
  const { control, handleSubmit, setError, formState } = useForm<ProfileForm>({
    resolver: zodResolver(profileSchema),
    values: { firstName: user.firstName, lastName: user.lastName, phoneNumber: user.phoneNumber ?? '' },
  })

  const save = useMutation({
    meta: { silent: true },
    mutationFn: (values: ProfileForm) =>
      authApi.updateProfile({
        firstName: values.firstName,
        lastName: values.lastName,
        phoneNumber: values.phoneNumber.trim() === '' ? null : values.phoneNumber.trim(),
      }),
    onSuccess: async () => {
      await refreshCurrentUser()
      toast.success('Profile saved.')
    },
    onError: (error) => {
      const unmatched = applyFieldErrors(error, ['firstName', 'lastName', 'phoneNumber'] as const, setError)
      setBanner(unmatched[0] ?? (toApiError(error).isValidation ? null : toApiError(error).message))
    },
  })

  return (
    <Card>
      <CardHeader title="Profile" slotProps={{ title: { variant: 'h5', component: 'h2' } }} />
      <CardContent>
        <form onSubmit={handleSubmit((values) => { setBanner(null); save.mutate(values) })} noValidate aria-label="Profile">
          <Stack spacing={2}>
            {banner ? <Alert severity="error" role="alert">{banner}</Alert> : null}
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
              <FormTextField control={control} name="firstName" label="First name" autoComplete="given-name" />
              <FormTextField control={control} name="lastName" label="Last name" autoComplete="family-name" />
            </Stack>
            <FormTextField control={control} name="phoneNumber" label="Phone" type="tel" autoComplete="tel" />
            <Box>
              <Typography variant="body2" color="textSecondary">Email: {user.email}</Typography>
              <Stack direction="row" spacing={0.75} sx={{ flexWrap: 'wrap', mt: 0.75 }} useFlexGap>
                {user.roles.map((role) => (
                  <Chip key={role} size="small" variant="outlined" label={humanizeRole(role)} />
                ))}
              </Stack>
            </Box>
            <Box>
              <Button type="submit" variant="contained" disabled={!formState.isDirty || save.isPending}>
                {save.isPending ? 'Saving…' : 'Save profile'}
              </Button>
            </Box>
          </Stack>
        </form>
      </CardContent>
    </Card>
  )
}

function PasswordCard() {
  const [banner, setBanner] = useState<string | null>(null)
  const { control, handleSubmit, reset, setError } = useForm<ChangePasswordForm>({
    resolver: zodResolver(changePasswordSchema),
    defaultValues: { currentPassword: '', newPassword: '', confirmPassword: '' },
  })
  const newPassword = useWatch({ control, name: 'newPassword' })
  const queryClient = useQueryClient()

  const change = useMutation({
    meta: { silent: true },
    mutationFn: (values: ChangePasswordForm) =>
      authApi.changePassword({ currentPassword: values.currentPassword, newPassword: values.newPassword }),
    onSuccess: async () => {
      reset()
      toast.success('Password changed. Your other devices have been signed out.')
      await queryClient.invalidateQueries({ queryKey: sessionsKey })
    },
    onError: (error) => {
      const unmatched = applyFieldErrors(error, ['currentPassword', 'newPassword'] as const, setError)
      setBanner(unmatched[0] ?? (toApiError(error).isValidation ? null : toApiError(error).message))
    },
  })

  return (
    <Card>
      <CardHeader
        title="Password"
        subheader="Changing it signs you out of every other device."
        slotProps={{ title: { variant: 'h5', component: 'h2' } }}
      />
      <CardContent>
        <form onSubmit={handleSubmit((values) => { setBanner(null); change.mutate(values) })} noValidate aria-label="Change password">
          <Stack spacing={2}>
            {banner ? <Alert severity="error" role="alert">{banner}</Alert> : null}
            <PasswordField control={control} name="currentPassword" label="Current password" autoComplete="current-password" />
            <PasswordField control={control} name="newPassword" label="New password" autoComplete="new-password" />
            <PasswordRequirements password={newPassword} />
            <PasswordField control={control} name="confirmPassword" label="Repeat new password" autoComplete="new-password" />
            <Box>
              <Button type="submit" variant="contained" disabled={change.isPending}>
                {change.isPending ? 'Changing…' : 'Change password'}
              </Button>
            </Box>
          </Stack>
        </form>
      </CardContent>
    </Card>
  )
}

function SessionsCard() {
  const timeZone = useAuthStore((s) => s.user?.organization?.timeZoneId)
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const sessions = useQuery({ queryKey: sessionsKey, queryFn: authApi.sessions })
  const revoke = useMutation({
    mutationFn: (id: string) => authApi.revokeSession(id),
    onSuccess: async () => {
      toast.success('Device signed out.')
      await queryClient.invalidateQueries({ queryKey: sessionsKey })
    },
  })

  async function signOutDevice(id: string, label: string) {
    const { confirmed } = await confirm({
      title: 'Sign out this device?',
      message: `${label} will be signed out immediately.`,
      confirmLabel: 'Sign out device',
      destructive: true,
    })
    if (confirmed) {
      revoke.mutate(id)
    }
  }

  return (
    <Card>
      <CardHeader
        title="Where you are signed in"
        subheader="Sign out any device you do not recognise."
        slotProps={{ title: { variant: 'h5', component: 'h2' } }}
      />
      <CardContent sx={{ pt: 0 }}>
        {sessions.isPending ? (
          <LoadingState variant="skeleton" lines={3} label="Loading sessions" />
        ) : sessions.isError ? (
          <ErrorState error={sessions.error} onRetry={() => void sessions.refetch()} />
        ) : (
          <List disablePadding aria-label="Active sessions">
            {sessions.data.map((session) => {
              const label = describeUserAgent(session.userAgent)
              const mobile = /Android|iOS|iPhone/.test(label)
              return (
                <ListItem
                  key={session.id}
                  divider
                  disableGutters
                  secondaryAction={
                    session.isCurrent ? (
                      <Chip label="This device" size="small" color="primary" />
                    ) : (
                      <Button size="small" color="error" onClick={() => void signOutDevice(session.id, label)} disabled={revoke.isPending}>
                        Sign out
                      </Button>
                    )
                  }
                >
                  <ListItemIcon>{mobile ? <PhoneIphoneIcon /> : <ComputerIcon />}</ListItemIcon>
                  <ListItemText
                    primary={label}
                    secondary={`${session.ipAddress ?? 'Unknown address'} · active ${formatRelative(session.lastSeenAt)} · signed in ${formatDateTime(session.createdAt, timeZone)}`}
                  />
                </ListItem>
              )
            })}
          </List>
        )}
      </CardContent>
    </Card>
  )
}

export default function AccountPage() {
  return (
    <>
      <PageHeader title="Account & security" subtitle="Your profile, password and signed-in devices." />
      <Grid container spacing={2.5}>
        <Grid size={{ xs: 12, lg: 6 }}>
          <Stack spacing={2.5}>
            <ProfileCard />
            <PasswordCard />
          </Stack>
        </Grid>
        <Grid size={{ xs: 12, lg: 6 }}>
          <SessionsCard />
        </Grid>
      </Grid>
    </>
  )
}
