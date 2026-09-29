import { zodResolver } from '@hookform/resolvers/zod'
import Alert from '@mui/material/Alert'
import Button from '@mui/material/Button'
import Link from '@mui/material/Link'
import Stack from '@mui/material/Stack'
import { useMutation } from '@tanstack/react-query'
import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router'
import { PasswordField } from '@/components/forms/FormFields'
import { toast } from '@/components/ui/toastStore'
import { toApiError } from '@/shared/api/problem'
import { paths } from '@/shared/constants/paths'
import { authApi } from '../api/authApi'
import { AuthLayout } from '../components/AuthLayout'
import { PasswordRequirements } from '../components/PasswordRequirements'
import { resetPasswordSchema, type ResetPasswordForm } from '../schemas/authSchemas'

export default function ResetPasswordPage() {
  const [params] = useSearchParams()
  const token = params.get('token') ?? ''
  const navigate = useNavigate()
  const [banner, setBanner] = useState<string | null>(null)
  const { control, handleSubmit } = useForm<ResetPasswordForm>({
    resolver: zodResolver(resetPasswordSchema),
    defaultValues: { newPassword: '', confirmPassword: '' },
  })
  const newPassword = useWatch({ control, name: 'newPassword' })

  const reset = useMutation({
    meta: { silent: true },
    mutationFn: (values: ResetPasswordForm) => authApi.resetPassword(token, values.newPassword),
    onSuccess: () => {
      toast.success('Password updated. Sign in with your new password.')
      navigate(paths.login, { replace: true })
    },
    onError: (error) => setBanner(toApiError(error).message),
  })

  if (!token) {
    return (
      <AuthLayout title="This link is not valid" footer={<Link component={RouterLink} to={paths.forgotPassword}>Request a new link</Link>}>
        <Alert severity="error">The password reset link is incomplete. Request a new one and use the link from the latest email.</Alert>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout title="Choose a new password" subtitle="You will be signed out of all your devices.">
      <form onSubmit={handleSubmit((values) => { setBanner(null); reset.mutate(values) })} noValidate>
        <Stack spacing={2.25}>
          {banner ? (
            <Alert severity="error" role="alert" action={<Button color="inherit" size="small" component={RouterLink} to={paths.forgotPassword}>New link</Button>}>
              {banner}
            </Alert>
          ) : null}
          <PasswordField control={control} name="newPassword" label="New password" autoComplete="new-password" autoFocus />
          <PasswordRequirements password={newPassword} />
          <PasswordField control={control} name="confirmPassword" label="Repeat new password" autoComplete="new-password" />
          <Button type="submit" variant="contained" size="large" disabled={reset.isPending}>
            {reset.isPending ? 'Saving…' : 'Update password'}
          </Button>
        </Stack>
      </form>
    </AuthLayout>
  )
}
