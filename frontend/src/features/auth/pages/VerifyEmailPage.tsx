import CheckCircleIcon from '@mui/icons-material/CheckCircle'
import ErrorOutlinedIcon from '@mui/icons-material/ErrorOutlined'
import Button from '@mui/material/Button'
import CircularProgress from '@mui/material/CircularProgress'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useMutation } from '@tanstack/react-query'
import { useEffect, useRef } from 'react'
import { Link as RouterLink, useSearchParams } from 'react-router'
import { toApiError } from '@/shared/api/problem'
import { paths } from '@/shared/constants/paths'
import { authApi } from '../api/authApi'
import { AuthLayout } from '../components/AuthLayout'

export default function VerifyEmailPage() {
  const [params] = useSearchParams()
  const token = params.get('token') ?? ''
  const started = useRef(false)

  const verify = useMutation({
    meta: { silent: true },
    mutationFn: () => authApi.verifyEmail(token),
  })

  useEffect(() => {
    // The link is single-use, and StrictMode runs effects twice in development: verify exactly once.
    if (token && !started.current) {
      started.current = true
      verify.mutate()
    }
  }, [token, verify])

  const missing = !token
  const failed = missing || verify.isError
  const message = missing ? 'This verification link is incomplete.' : verify.isError ? toApiError(verify.error).message : ''

  return (
    <AuthLayout title={verify.isSuccess ? 'Email verified' : failed ? 'We could not verify your email' : 'Verifying your email'}>
      <Stack spacing={2.5} role="status" aria-live="polite" sx={{ alignItems: 'center', textAlign: 'center' }}>
        {verify.isSuccess ? (
          <>
            <CheckCircleIcon color="success" sx={{ fontSize: 56 }} aria-hidden />
            <Typography>Your email address is confirmed. You can now sign in.</Typography>
            <Button component={RouterLink} to={paths.login} variant="contained" size="large">
              Continue to sign in
            </Button>
          </>
        ) : failed ? (
          <>
            <ErrorOutlinedIcon color="error" sx={{ fontSize: 56 }} aria-hidden />
            <Typography>{message}</Typography>
            <Typography variant="body2" color="textSecondary">
              Links can only be used once and expire after 48 hours. If you already verified, just sign in; otherwise sign in to
              request a new link.
            </Typography>
            <Button component={RouterLink} to={paths.login} variant="outlined">
              Go to sign in
            </Button>
          </>
        ) : (
          <>
            <CircularProgress aria-label="Verifying" />
            <Typography color="textSecondary">One moment…</Typography>
          </>
        )}
      </Stack>
    </AuthLayout>
  )
}
