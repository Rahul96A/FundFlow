import BlockIcon from '@mui/icons-material/Block'
import LockOpenIcon from '@mui/icons-material/LockOpen'
import MailIcon from '@mui/icons-material/Mail'
import MoreVertIcon from '@mui/icons-material/MoreVert'
import PersonAddIcon from '@mui/icons-material/PersonAdd'
import RestoreIcon from '@mui/icons-material/Restore'
import Avatar from '@mui/material/Avatar'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Chip from '@mui/material/Chip'
import IconButton from '@mui/material/IconButton'
import ListItemIcon from '@mui/material/ListItemIcon'
import Menu from '@mui/material/Menu'
import MenuItem from '@mui/material/MenuItem'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Tooltip from '@mui/material/Tooltip'
import Typography from '@mui/material/Typography'
import { useCallback, useMemo, useState } from 'react'
import { useAuthStore, usePermission } from '@/app/store/authStore'
import { DataTable, type DataColumn } from '@/components/tables/DataTable'
import { FilterBar } from '@/components/tables/FilterBar'
import { useConfirm } from '@/components/ui/confirmStore'
import { EmptyState } from '@/components/ui/States'
import { PageHeader } from '@/components/ui/PageHeader'
import { SearchBox } from '@/components/ui/SearchBox'
import { toast } from '@/components/ui/toastStore'
import { useRoles } from '@/features/roles/hooks/useRoles'
import { Permissions } from '@/shared/constants/permissions'
import { useTableParams } from '@/shared/hooks/useTableParams'
import { formatDateTime, formatRelative, humanizeRole, initials } from '@/shared/utils/format'
import { InviteUserDrawer } from '../components/InviteUserDrawer'
import { UserDrawer } from '../components/UserDrawer'
import { UserStatusBadge } from '../components/UserStatusBadge'
import { userStatusLabel } from '../userStatus'
import { useUserAction, useUsers } from '../hooks/useUsers'
import { USER_STATUSES, type UserStatus, type UserSummary } from '../types/user.types'

function isUserStatus(value: string): value is UserStatus {
  return (USER_STATUSES as readonly string[]).includes(value)
}

export default function UsersPage() {
  const currentUserId = useAuthStore((s) => s.user?.id)
  const timeZone = useAuthStore((s) => s.user?.organization?.timeZoneId)
  const canManage = usePermission(Permissions.User.Manage)
  const canReadRoles = usePermission(Permissions.Role.Read)
  const confirm = useConfirm()
  const action = useUserAction()

  const table = useTableParams(['status', 'roleId'] as const, { sortBy: 'createdAt', sortDirection: 'desc' })
  const status = isUserStatus(table.filters.status) ? table.filters.status : undefined
  const roleId = table.filters.roleId || undefined

  const users = useUsers({
    page: table.page,
    pageSize: table.pageSize,
    sortBy: table.sortBy,
    sortDirection: table.sortDirection,
    search: table.q || undefined,
    status,
    roleId,
  })
  const roles = useRoles(canReadRoles)

  const [selected, setSelected] = useState<string[]>([])
  const [inviteOpen, setInviteOpen] = useState(false)
  const [openUserId, setOpenUserId] = useState<string | null>(null)
  const [menu, setMenu] = useState<{ anchor: HTMLElement; user: UserSummary } | null>(null)

  const setSearch = useCallback((value: string) => table.update({ q: value }), [table])

  async function runAction(user: UserSummary, kind: 'deactivate' | 'reactivate' | 'unlock' | 'resendInvitation') {
    setMenu(null)
    if (kind === 'deactivate') {
      const { confirmed } = await confirm({
        title: `Deactivate ${user.fullName}?`,
        message: 'They will be signed out everywhere immediately and cannot sign in until you reactivate them.',
        confirmLabel: 'Deactivate',
        destructive: true,
      })
      if (!confirmed) {
        return
      }
    }
    action.mutate(
      { id: user.id, action: kind },
      {
        onSuccess: () =>
          toast.success(
            { deactivate: 'User deactivated.', reactivate: 'User reactivated.', unlock: 'User unlocked.', resendInvitation: 'Invitation sent again.' }[kind],
          ),
      },
    )
  }

  async function bulk(kind: 'deactivate' | 'reactivate', ids: string[]) {
    const targets = ids.filter((id) => id !== currentUserId)
    const { confirmed } = await confirm({
      title: `${kind === 'deactivate' ? 'Deactivate' : 'Reactivate'} ${targets.length} user${targets.length === 1 ? '' : 's'}?`,
      message:
        kind === 'deactivate'
          ? 'They will be signed out everywhere immediately. Your own account and the last administrator are skipped automatically.'
          : 'They will be able to sign in again.',
      confirmLabel: kind === 'deactivate' ? 'Deactivate' : 'Reactivate',
      destructive: kind === 'deactivate',
    })
    if (!confirmed) {
      return
    }
    const results = await Promise.allSettled(targets.map((id) => action.mutateAsync({ id, action: kind })))
    const failed = results.filter((r) => r.status === 'rejected').length
    setSelected([])
    if (failed === 0) {
      toast.success(`${targets.length} user${targets.length === 1 ? '' : 's'} ${kind === 'deactivate' ? 'deactivated' : 'reactivated'}.`)
    } else {
      toast.warning(`${targets.length - failed} succeeded, ${failed} could not be changed (see the messages above).`)
    }
  }

  const columns = useMemo<DataColumn<UserSummary>[]>(
    () => [
      {
        id: 'name',
        header: 'User',
        sortKey: 'name',
        required: true,
        csv: (u) => u.fullName,
        renderCell: (u) => (
          <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center' }}>
            <Avatar sx={{ width: 32, height: 32, fontSize: 13, bgcolor: 'action.selected', color: 'text.primary' }}>{initials(u.fullName)}</Avatar>
            <Box sx={{ minWidth: 0 }}>
              <Typography variant="body2" sx={{ fontWeight: 600 }} noWrap>
                {u.fullName}
                {u.id === currentUserId ? <Chip label="You" size="small" sx={{ ml: 1, height: 18 }} /> : null}
              </Typography>
              <Typography variant="caption" color="textSecondary" noWrap component="p">
                {u.email}
              </Typography>
            </Box>
          </Stack>
        ),
      },
      { id: 'email', header: 'Email', sortKey: 'email', hiddenByDefault: true, hideBelow: 'md', csv: (u) => u.email, renderCell: (u) => u.email },
      {
        id: 'roles',
        header: 'Roles',
        hideBelow: 'md',
        csv: (u) => u.roles.join('; '),
        renderCell: (u) => (
          <Stack direction="row" spacing={0.5} useFlexGap sx={{ flexWrap: 'wrap' }}>
            {u.roles.map((role) => (
              <Chip key={role} label={humanizeRole(role)} size="small" variant="outlined" />
            ))}
          </Stack>
        ),
      },
      { id: 'status', header: 'Status', csv: (u) => userStatusLabel(u.status), renderCell: (u) => <UserStatusBadge status={u.status} /> },
      {
        id: 'lastLogin',
        header: 'Last sign-in',
        sortKey: 'lastLoginAt',
        hideBelow: 'lg',
        csv: (u) => u.lastLoginAt ?? '',
        renderCell: (u) =>
          u.lastLoginAt ? (
            <Tooltip title={formatDateTime(u.lastLoginAt, timeZone)}>
              <span>{formatRelative(u.lastLoginAt)}</span>
            </Tooltip>
          ) : (
            <Typography variant="body2" color="textSecondary">Never</Typography>
          ),
      },
      {
        id: 'created',
        header: 'Created',
        sortKey: 'createdAt',
        hideBelow: 'lg',
        hiddenByDefault: true,
        csv: (u) => u.createdAt,
        renderCell: (u) => formatDateTime(u.createdAt, timeZone),
      },
      {
        id: 'actions',
        header: '',
        required: true,
        align: 'right',
        width: 56,
        renderCell: (u) => (
          <IconButton
            aria-label={`Actions for ${u.fullName}`}
            aria-haspopup="menu"
            size="small"
            onClick={(event) => {
              event.stopPropagation()
              setMenu({ anchor: event.currentTarget, user: u })
            }}
          >
            <MoreVertIcon fontSize="small" />
          </IconButton>
        ),
      },
    ],
    [currentUserId, timeZone],
  )

  const menuUser = menu?.user

  return (
    <>
      <PageHeader
        title="Users"
        subtitle="People who can sign in to your organization, and what they can do."
        actions={
          canManage && canReadRoles ? (
            <Button variant="contained" startIcon={<PersonAddIcon />} onClick={() => setInviteOpen(true)}>
              Invite user
            </Button>
          ) : undefined
        }
      />

      <DataTable
        ariaLabel="Users"
        columns={columns}
        rows={users.data?.items ?? []}
        getRowId={(u) => u.id}
        getRowLabel={(u) => u.fullName}
        loading={users.isPending}
        fetching={users.isFetching && !users.isPending}
        error={users.error}
        onRetry={() => void users.refetch()}
        page={table.page}
        pageSize={table.pageSize}
        totalCount={users.data?.totalCount ?? 0}
        onPageChange={(page) => table.update({ page })}
        onPageSizeChange={(pageSize) => table.update({ pageSize })}
        sortBy={table.sortBy}
        sortDirection={table.sortDirection}
        onSortChange={(sortBy, sortDirection) => table.update({ sortBy, sortDirection })}
        selectable={canManage}
        selectedIds={selected}
        onSelectionChange={setSelected}
        renderBulkActions={(ids) => (
          <>
            <Button size="small" color="error" startIcon={<BlockIcon />} onClick={() => void bulk('deactivate', ids)}>
              Deactivate
            </Button>
            <Button size="small" startIcon={<RestoreIcon />} onClick={() => void bulk('reactivate', ids)}>
              Reactivate
            </Button>
          </>
        )}
        onRowClick={(u) => setOpenUserId(u.id)}
        exportFileName="users.csv"
        toolbar={
          <FilterBar canReset={table.hasActiveFilters} onReset={table.clearFilters}>
            <SearchBox value={table.q} onChange={setSearch} placeholder="Name or email" label="Search users" />
            <TextField
              select
              label="Status"
              value={status ?? ''}
              onChange={(event) => table.update({ status: event.target.value })}
              fullWidth={false} sx={{ minWidth: 160 }}
            >
              <MenuItem value="">All statuses</MenuItem>
              {USER_STATUSES.map((s) => (
                <MenuItem key={s} value={s}>
                  {userStatusLabel(s)}
                </MenuItem>
              ))}
            </TextField>
            {canReadRoles ? (
              <TextField
                select
                label="Role"
                value={roleId ?? ''}
                onChange={(event) => table.update({ roleId: event.target.value })}
                fullWidth={false} sx={{ minWidth: 200 }}
              >
                <MenuItem value="">All roles</MenuItem>
                {(roles.data ?? []).map((role) => (
                  <MenuItem key={role.id} value={role.id}>
                    {role.isSystem ? humanizeRole(role.name) : role.name}
                  </MenuItem>
                ))}
              </TextField>
            ) : null}
          </FilterBar>
        }
        emptyState={
          table.hasActiveFilters ? (
            <EmptyState
              title="No users match these filters"
              description="Try a different search or clear the filters."
              action={<Button onClick={table.clearFilters}>Clear filters</Button>}
            />
          ) : (
            <EmptyState
              title="No users yet"
              description="Invite your colleagues so they can help run your fundraising."
              action={canManage && canReadRoles ? <Button variant="contained" onClick={() => setInviteOpen(true)}>Invite user</Button> : undefined}
            />
          )
        }
      />

      <Menu anchorEl={menu?.anchor} open={Boolean(menu)} onClose={() => setMenu(null)}>
        <MenuItem onClick={() => { setOpenUserId(menuUser!.id); setMenu(null) }}>View &amp; edit</MenuItem>
        {canManage && menuUser?.status === 'Invited' ? (
          <MenuItem onClick={() => void runAction(menuUser, 'resendInvitation')}>
            <ListItemIcon><MailIcon fontSize="small" /></ListItemIcon>
            Resend invitation
          </MenuItem>
        ) : null}
        {canManage && menuUser?.status === 'Locked' ? (
          <MenuItem onClick={() => void runAction(menuUser, 'unlock')}>
            <ListItemIcon><LockOpenIcon fontSize="small" /></ListItemIcon>
            Unlock
          </MenuItem>
        ) : null}
        {canManage && menuUser && menuUser.status !== 'Deactivated' && menuUser.id !== currentUserId ? (
          <MenuItem onClick={() => void runAction(menuUser, 'deactivate')} sx={{ color: 'error.main' }}>
            <ListItemIcon><BlockIcon fontSize="small" color="error" /></ListItemIcon>
            Deactivate
          </MenuItem>
        ) : null}
        {canManage && menuUser?.status === 'Deactivated' ? (
          <MenuItem onClick={() => void runAction(menuUser, 'reactivate')}>
            <ListItemIcon><RestoreIcon fontSize="small" /></ListItemIcon>
            Reactivate
          </MenuItem>
        ) : null}
      </Menu>

      <InviteUserDrawer open={inviteOpen} onClose={() => setInviteOpen(false)} />
      <UserDrawer userId={openUserId} onClose={() => setOpenUserId(null)} canManage={canManage} canReadRoles={canReadRoles} />
    </>
  )
}
