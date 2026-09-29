import { zodResolver } from '@hookform/resolvers/zod'
import Alert from '@mui/material/Alert'
import Button from '@mui/material/Button'
import Link from '@mui/material/Link'
import Stack from '@mui/material/Stack'
import { useMutation } from '@tanstack/react-query'
import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router'
import { FormTextField, PasswordField } from '@/components/forms/FormFields'
import { toast } from '@/components/ui/toastStore'
import { toApiError } from '@/shared/api/problem'
import { paths } from '@/shared/constants/paths'
import { authApi } from '../api/authApi'
import { AuthLayout } from '../components/AuthLayout'
import { PasswordRequirements } from '../components/PasswordRequirements'
import { acceptInvitationSchema, type AcceptInvitationForm } from '../schemas/authSchemas'

export default function AcceptInvitationPage() {
  const [params] = useSearchParams()
  const token = params.get('token') ?? ''
  const navigate = useNavigate()
  const [banner, setBanner] = useState<string | null>(null)
  const { control, handleSubmit } = useForm<AcceptInvitationForm>({
    resolver: zodResolver(acceptInvitationSchema),
    defaultValues: { firstName: '', lastName: '', password: '', confirmPassword: '' },
  })
  const password = useWatch({ control, name: 'password' })

  const accept = useMutation({
    meta: { silent: true },
    mutationFn: (values: AcceptInvitationForm) =>
      authApi.acceptInvitation({
        token,
        password: values.password,
        firstName: values.firstName || null,
        lastName: values.lastName || null,
      }),
    onSuccess: () => {
      toast.success('Welcome aboard. Sign in to get started.')
      navigate(paths.login, { replace: true })
    },
    onError: (error) => setBanner(toApiError(error).message),
  })

  if (!token) {
    return (
      <AuthLayout title="This invitation link is not valid" footer={<Link component={RouterLink} to={paths.login}>Go to sign in</Link>}>
        <Alert severity="error">The invitation link is incomplete. Ask your administrator to send it again.</Alert>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout title="Accept your invitation" subtitle="Choose a password to activate your account.">
      <form onSubmit={handleSubmit((values) => { setBanner(null); accept.mutate(values) })} noValidate>
        <Stack spacing={2.25}>
          {banner ? <Alert severity="error" role="alert">{banner}</Alert> : null}
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <FormTextField control={control} name="firstName" label="First name (optional)" autoComplete="given-name" />
            <FormTextField control={control} name="lastName" label="Last name (optional)" autoComplete="family-name" />
          </Stack>
          <PasswordField control={control} name="password" label="Password" autoComplete="new-password" autoFocus />
          <PasswordRequirements password={password} />
          <PasswordField control={control} name="confirmPassword" label="Repeat password" autoComplete="new-password" />
          <Button type="submit" variant="contained" size="large" disabled={accept.isPending}>
            {accept.isPending ? 'Activating…' : 'Activate account'}
          </Button>
        </Stack>
      </form>
    </AuthLayout>
  )
}
