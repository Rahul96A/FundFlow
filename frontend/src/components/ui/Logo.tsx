import Box from '@mui/material/Box'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'

interface LogoProps {
  /** Icon only (collapsed sidebar). */
  compact?: boolean
  inverted?: boolean
  size?: number
}

export function Logo({ compact = false, inverted = false, size = 32 }: LogoProps) {
  return (
    <Stack direction="row" spacing={1.25} sx={{ alignItems: 'center' }}>
      <Box
        component="svg"
        viewBox="0 0 32 32"
        role="img"
        aria-label="FundFlow"
        sx={{ width: size, height: size, flexShrink: 0 }}
      >
        <rect width="32" height="32" rx="8" fill={inverted ? '#ffffff' : '#1f5eff'} />
        <path
          d="M9 21.5c3.2 0 3.6-11 7-11s3.8 11 7 11"
          fill="none"
          stroke={inverted ? '#1f5eff' : '#ffffff'}
          strokeWidth="2.6"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
        <circle cx="16" cy="10.5" r="2.1" fill={inverted ? '#1f5eff' : '#ffffff'} />
      </Box>
      {compact ? null : (
        <Typography
          variant="h5"
          component="span"
          sx={{ fontWeight: 750, letterSpacing: '-0.02em', color: inverted ? '#ffffff' : 'text.primary' }}
        >
          FundFlow
        </Typography>
      )}
    </Stack>
  )
}
