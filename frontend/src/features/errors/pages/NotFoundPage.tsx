import SearchOffIcon from '@mui/icons-material/SearchOff'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import { Link as RouterLink } from 'react-router'
import { EmptyState } from '@/components/ui/States'
import { paths } from '@/shared/constants/paths'

export default function NotFoundPage() {
  return (
    <Box sx={{ minHeight: '60vh', display: 'grid', placeItems: 'center' }}>
      <EmptyState
        icon={<SearchOffIcon sx={{ fontSize: 56 }} />}
        title="Page not found"
        description="The page you are looking for does not exist or has moved."
        action={
          <Button component={RouterLink} to={paths.root} variant="contained">
            Go to the dashboard
          </Button>
        }
      />
    </Box>
  )
}
