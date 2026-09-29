import AddIcon from '@mui/icons-material/Add'
import Button from '@mui/material/Button'
import Chip from '@mui/material/Chip'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import Table from '@mui/material/Table'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableContainer from '@mui/material/TableContainer'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import Typography from '@mui/material/Typography'
import { useMemo, useState } from 'react'
import { usePermission } from '@/app/store/authStore'
import { PageHeader } from '@/components/ui/PageHeader'
import { SearchBox } from '@/components/ui/SearchBox'
import { EmptyState, ErrorState, LoadingState } from '@/components/ui/States'
import { Permissions } from '@/shared/constants/permissions'
import { humanizeRole } from '@/shared/utils/format'
import { NEW_ROLE, RoleDrawer } from '../components/RoleDrawer'
import { useRoles } from '../hooks/useRoles'

export default function RolesPage() {
  const canManage = usePermission(Permissions.Role.Manage)
  const roles = useRoles()
  const [search, setSearch] = useState('')
  const [openId, setOpenId] = useState<string | typeof NEW_ROLE | null>(null)

  const visible = useMemo(() => {
    const term = search.trim().toLowerCase()
    return (roles.data ?? []).filter(
      (role) => term === '' || role.name.toLowerCase().includes(term) || (role.description ?? '').toLowerCase().includes(term),
    )
  }, [roles.data, search])

  return (
    <>
      <PageHeader
        title="Roles & permissions"
        subtitle="A role is a named bundle of permissions. Assign roles to users; never share logins."
        actions={
          canManage ? (
            <Button variant="contained" startIcon={<AddIcon />} onClick={() => setOpenId(NEW_ROLE)}>
              New role
            </Button>
          ) : undefined
        }
      />

      <Paper variant="outlined">
        <Stack sx={{ p: 2 }}>
          <SearchBox value={search} onChange={setSearch} placeholder="Role name or description" label="Search roles" />
        </Stack>
        {roles.isPending ? (
          <LoadingState variant="skeleton" lines={6} label="Loading roles" />
        ) : roles.isError ? (
          <ErrorState error={roles.error} onRetry={() => void roles.refetch()} />
        ) : visible.length === 0 ? (
          <EmptyState title="No roles match your search" />
        ) : (
          <TableContainer>
            <Table size="small" aria-label="Roles">
              <TableHead>
                <TableRow>
                  <TableCell>Role</TableCell>
                  <TableCell sx={{ display: { xs: 'none', md: 'table-cell' } }}>Description</TableCell>
                  <TableCell>Type</TableCell>
                  <TableCell align="right">Permissions</TableCell>
                  <TableCell align="right">Users</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {visible.map((role) => (
                  <TableRow
                    key={role.id}
                    hover
                    tabIndex={0}
                    onClick={() => setOpenId(role.id)}
                    onKeyDown={(event) => {
                      if (event.key === 'Enter' || event.key === ' ') {
                        event.preventDefault()
                        setOpenId(role.id)
                      }
                    }}
                    sx={{ cursor: 'pointer' }}
                    aria-label={`Open role ${role.name}`}
                  >
                    <TableCell>
                      <Typography variant="body2" sx={{ fontWeight: 600 }}>
                        {role.isSystem ? humanizeRole(role.name) : role.name}
                      </Typography>
                    </TableCell>
                    <TableCell sx={{ display: { xs: 'none', md: 'table-cell' }, maxWidth: 420 }}>
                      <Typography variant="body2" color="textSecondary" noWrap>
                        {role.description ?? '—'}
                      </Typography>
                    </TableCell>
                    <TableCell>
                      <Chip size="small" label={role.isSystem ? 'Standard' : 'Custom'} variant={role.isSystem ? 'outlined' : 'filled'} color={role.isSystem ? 'default' : 'primary'} />
                    </TableCell>
                    <TableCell align="right">{role.permissionCount}</TableCell>
                    <TableCell align="right">{role.userCount}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        )}
      </Paper>

      <RoleDrawer roleId={openId} onClose={() => setOpenId(null)} canManage={canManage} />
    </>
  )
}
