import { zodResolver } from '@hookform/resolvers/zod'
import Alert from '@mui/material/Alert'
import Button from '@mui/material/Button'
import Link from '@mui/material/Link'
import Stack from '@mui/material/Stack'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link as RouterLink, useLocation, useNavigate } from 'react-router'
import { landingPath } from '@/components/layout/navigation'
import { FormTextField, PasswordField } from '@/components/forms/FormFields'
import { toast } from '@/components/ui/toastStore'
import { toApiError } from '@/shared/api/problem'
import { paths } from '@/shared/constants/paths'
import { authApi } from '../api/authApi'
import { AuthLayout } from '../components/AuthLayout'
import { useLogin } from '../hooks/useAuthActions'
import { loginSchema, type LoginForm } from '../schemas/authSchemas'
import { safeReturnPath } from '../utils/returnPath'

export default function LoginPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const login = useLogin()
  const [problem, setProblem] = useState<{ message: string; code?: string } | null>(null)
  const [resending, setResending] = useState(false)

  const { control, handleSubmit, getValues } = useForm<LoginForm>({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: '', password: '' },
  })

  const onSubmit = handleSubmit((values) => {
    setProblem(null)
    login.mutate(values, {
      onSuccess: (user) => {
        const fallback = landingPath(user.isPlatformUser)
        navigate(safeReturnPath((location.state as { from?: string } | null)?.from, fallback), { replace: true })
      },
      onError: (error) => {
        const apiError = toApiError(error)
        setProblem({ message: apiError.message, code: apiError.code })
      },
    })
  })

  async function resendVerification() {
    setResending(true)
    try {
      await authApi.resendVerification(getValues('email'))
      toast.success('If that address needs verifying, a new link is on its way.')
    } catch (error) {
      toast.error(toApiError(error).message)
    } finally {
      setResending(false)
    }
  }

  return (
    <AuthLayout
      title="Welcome back"
      subtitle="Sign in to your FundFlow workspace."
      footer={
        <>
          New to FundFlow?{' '}
          <Link component={RouterLink} to={paths.register}>
            Create your organization
          </Link>
        </>
      }
    >
      <form onSubmit={onSubmit} noValidate>
        <Stack spacing={2.5}>
          {problem ? (
            <Alert
              severity={problem.code === 'account_locked' || problem.code === 'account_disabled' ? 'warning' : 'error'}
              role="alert"
              action={
                problem.code === 'email_not_verified' ? (
                  <Button color="inherit" size="small" onClick={resendVerification} disabled={resending}>
                    Resend link
                  </Button>
                ) : undefined
              }
            >
              {problem.message}
            </Alert>
          ) : null}
          <FormTextField control={control} name="email" label="Email address" type="email" autoComplete="username" autoFocus />
          <PasswordField control={control} name="password" label="Password" autoComplete="current-password" />
          <Link component={RouterLink} to={paths.forgotPassword} variant="body2" sx={{ alignSelf: 'flex-end', mt: -1 }}>
            Forgot your password?
          </Link>
          <Button type="submit" variant="contained" size="large" disabled={login.isPending}>
            {login.isPending ? 'Signing in…' : 'Sign in'}
          </Button>
        </Stack>
      </form>
    </AuthLayout>
  )
}
