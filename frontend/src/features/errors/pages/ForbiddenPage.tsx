import LockOutlinedIcon from '@mui/icons-material/LockOutlined'
import Button from '@mui/material/Button'
import { Link as RouterLink } from 'react-router'
import { EmptyState } from '@/components/ui/States'
import { paths } from '@/shared/constants/paths'

export function ForbiddenPage() {
  return (
    <EmptyState
      icon={<LockOutlinedIcon sx={{ fontSize: 56 }} />}
      title="You do not have access to this page"
      description="Your role does not include the permission this page needs. If you think that is a mistake, ask an administrator of your organization."
      action={
        <Button component={RouterLink} to={paths.root} variant="contained">
          Back to home
        </Button>
      }
    />
  )
}

export default ForbiddenPage
