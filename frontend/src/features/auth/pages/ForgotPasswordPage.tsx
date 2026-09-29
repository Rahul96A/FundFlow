import { zodResolver } from '@hookform/resolvers/zod'
import MarkEmailReadIcon from '@mui/icons-material/MarkEmailRead'
import Button from '@mui/material/Button'
import Link from '@mui/material/Link'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useMutation } from '@tanstack/react-query'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link as RouterLink } from 'react-router'
import { FormTextField } from '@/components/forms/FormFields'
import { paths } from '@/shared/constants/paths'
import { authApi } from '../api/authApi'
import { AuthLayout } from '../components/AuthLayout'
import { forgotPasswordSchema, type ForgotPasswordForm } from '../schemas/authSchemas'

export default function ForgotPasswordPage() {
  const [sentTo, setSentTo] = useState<string | null>(null)
  const { control, handleSubmit } = useForm<ForgotPasswordForm>({
    resolver: zodResolver(forgotPasswordSchema),
    defaultValues: { email: '' },
  })
  const request = useMutation({
    mutationFn: (values: ForgotPasswordForm) => authApi.forgotPassword(values.email),
    onSuccess: (_data, values) => setSentTo(values.email),
  })

  const back = (
    <Link component={RouterLink} to={paths.login}>
      Back to sign in
    </Link>
  )

  if (sentTo) {
    return (
      <AuthLayout title="Check your inbox" footer={back}>
        <Stack spacing={2} sx={{ alignItems: 'center', textAlign: 'center' }}>
          <MarkEmailReadIcon color="primary" sx={{ fontSize: 56 }} aria-hidden />
          <Typography>
            If an account exists for <strong>{sentTo}</strong>, we have sent a link to choose a new password. It expires in one
            hour.
          </Typography>
        </Stack>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout title="Forgot your password?" subtitle="Enter your email and we will send you a link to choose a new one." footer={back}>
      <form onSubmit={handleSubmit((values) => request.mutate(values))} noValidate>
        <Stack spacing={2.5}>
          <FormTextField control={control} name="email" label="Email address" type="email" autoComplete="email" autoFocus />
          <Button type="submit" variant="contained" size="large" disabled={request.isPending}>
            {request.isPending ? 'Sending…' : 'Send reset link'}
          </Button>
        </Stack>
      </form>
    </AuthLayout>
  )
}
