import { zodResolver } from '@hookform/resolvers/zod'
import MarkEmailReadIcon from '@mui/icons-material/MarkEmailRead'
import Alert from '@mui/material/Alert'
import Button from '@mui/material/Button'
import Link from '@mui/material/Link'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useMutation } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { Link as RouterLink } from 'react-router'
import { FormSelect, FormTextField, PasswordField } from '@/components/forms/FormFields'
import { toast } from '@/components/ui/toastStore'
import { applyFieldErrors, toApiError } from '@/shared/api/problem'
import { paths } from '@/shared/constants/paths'
import { COMMON_CURRENCIES } from '@/shared/utils/regional'
import { authApi } from '../api/authApi'
import { AuthLayout } from '../components/AuthLayout'
import { PasswordRequirements } from '../components/PasswordRequirements'
import { registerSchema, type RegisterForm } from '../schemas/authSchemas'

const FIELDS = ['organizationName', 'firstName', 'lastName', 'email', 'password', 'currencyCode'] as const

export default function RegisterPage() {
  const [registeredEmail, setRegisteredEmail] = useState<string | null>(null)
  const [banner, setBanner] = useState<string | null>(null)
  const timeZoneId = useMemo(() => new Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC', [])

  const { control, handleSubmit, setError } = useForm<RegisterForm>({
    resolver: zodResolver(registerSchema),
    defaultValues: {
      organizationName: '',
      firstName: '',
      lastName: '',
      email: '',
      password: '',
      confirmPassword: '',
      currencyCode: 'USD',
    },
  })
  const password = useWatch({ control, name: 'password' })

  const register = useMutation({
    meta: { silent: true },
    mutationFn: (values: RegisterForm) =>
      authApi.registerOrganization({
        organizationName: values.organizationName,
        firstName: values.firstName,
        lastName: values.lastName,
        email: values.email,
        password: values.password,
        currencyCode: values.currencyCode,
        timeZoneId,
      }),
    onSuccess: (result) => setRegisteredEmail(result.email),
    onError: (error) => {
      const unmatched = applyFieldErrors(error, FIELDS, setError)
      const apiError = toApiError(error)
      // Field-level problems are shown on the field; anything else (rate limit, outage) goes in the banner.
      setBanner(unmatched.length > 0 || !apiError.isValidation ? (unmatched[0] ?? apiError.message) : null)
    },
  })

  if (registeredEmail) {
    return (
      <AuthLayout title="Check your inbox" footer={<Link component={RouterLink} to={paths.login}>Back to sign in</Link>}>
        <Stack spacing={2.5} sx={{ alignItems: 'center', textAlign: 'center' }}>
          <MarkEmailReadIcon color="primary" sx={{ fontSize: 56 }} aria-hidden />
          <Typography>
            We sent a verification link to <strong>{registeredEmail}</strong>. Open it to activate your organization, then
            sign in.
          </Typography>
          <Typography variant="body2" color="textSecondary">
            The link is valid for 48 hours. Nothing arrived? Check your spam folder or request another.
          </Typography>
          <Button
            variant="outlined"
            onClick={async () => {
              try {
                await authApi.resendVerification(registeredEmail)
                toast.success('Another verification email is on its way.')
              } catch (error) {
                toast.error(toApiError(error).message)
              }
            }}
          >
            Resend the email
          </Button>
        </Stack>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout
      title="Create your organization"
      subtitle="Set up your FundFlow workspace in a minute. No card required."
      footer={
        <>
          Already have an account?{' '}
          <Link component={RouterLink} to={paths.login}>
            Sign in
          </Link>
        </>
      }
    >
      <form onSubmit={handleSubmit((values) => { setBanner(null); register.mutate(values) })} noValidate>
        <Stack spacing={2.25}>
          {banner ? <Alert severity="error" role="alert">{banner}</Alert> : null}
          <FormTextField control={control} name="organizationName" label="Organization name" autoComplete="organization" autoFocus />
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <FormTextField control={control} name="firstName" label="First name" autoComplete="given-name" />
            <FormTextField control={control} name="lastName" label="Last name" autoComplete="family-name" />
          </Stack>
          <FormTextField control={control} name="email" label="Work email" type="email" autoComplete="email" />
          <FormSelect
            control={control}
            name="currencyCode"
            label="Currency"
            options={COMMON_CURRENCIES}
            helperText="Used for donations and reports. You can change it later."
          />
          <PasswordField control={control} name="password" label="Password" autoComplete="new-password" />
          <PasswordRequirements password={password} />
          <PasswordField control={control} name="confirmPassword" label="Repeat password" autoComplete="new-password" />
          <Typography variant="caption" color="textSecondary">
            By creating an account you agree to use FundFlow in line with your organization&rsquo;s policies. Your time zone (
            {timeZoneId}) is saved as the organization default.
          </Typography>
          <Button type="submit" variant="contained" size="large" disabled={register.isPending}>
            {register.isPending ? 'Creating…' : 'Create organization'}
          </Button>
        </Stack>
      </form>
    </AuthLayout>
  )
}
