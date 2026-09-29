import BlockIcon from '@mui/icons-material/Block'
import RestoreIcon from '@mui/icons-material/Restore'
import Button from '@mui/material/Button'
import MenuItem from '@mui/material/MenuItem'
import TextField from '@mui/material/TextField'
import Tooltip from '@mui/material/Tooltip'
import Typography from '@mui/material/Typography'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useMemo } from 'react'
import { useAuthStore } from '@/app/store/authStore'
import { DataTable, type DataColumn } from '@/components/tables/DataTable'
import { FilterBar } from '@/components/tables/FilterBar'
import { useConfirm } from '@/components/ui/confirmStore'
import { PageHeader } from '@/components/ui/PageHeader'
import { SearchBox } from '@/components/ui/SearchBox'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { toast } from '@/components/ui/toastStore'
import { http } from '@/shared/api/http'
import { useTableParams } from '@/shared/hooks/useTableParams'
import type { Paged } from '@/shared/types/api'
import { formatDate } from '@/shared/utils/format'

interface PlatformOrganization {
  id: string
  name: string
  slug: string
  status: 'Active' | 'Suspended'
  contactEmail: string
  userCount: number
  createdAt: string
  suspendedAt: string | null
  suspensionReason: string | null
}

const key = ['platform', 'organizations'] as const

export default function PlatformOrganizationsPage() {
  const timeZone = useAuthStore((s) => s.user?.organization?.timeZoneId)
  const confirm = useConfirm()
  const queryClient = useQueryClient()
  const table = useTableParams(['status'] as const, { sortBy: 'createdAt', sortDirection: 'desc' })
  const status = table.filters.status === 'Active' || table.filters.status === 'Suspended' ? table.filters.status : undefined

  const params = {
    page: table.page,
    pageSize: table.pageSize,
    sortBy: table.sortBy,
    sortDirection: table.sortDirection,
    search: table.q || undefined,
    status,
  }
  const organizations = useQuery({
    queryKey: [...key, params],
    queryFn: () => http.get<Paged<PlatformOrganization>>('/platform/organizations', { params }).then((r) => r.data),
    placeholderData: keepPreviousData,
  })

  const suspend = useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) => http.post(`/platform/organizations/${id}/suspend`, { reason }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: key }),
  })
  const activate = useMutation({
    mutationFn: (id: string) => http.post(`/platform/organizations/${id}/activate`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: key }),
  })

  const setSearch = useCallback((value: string) => table.update({ q: value }), [table])

  const { mutate: suspendOrg, isPending: suspending } = suspend
  const { mutate: activateOrg, isPending: activating } = activate

  const handleSuspend = useCallback(
    async (org: PlatformOrganization) => {
      const { confirmed, reason } = await confirm({
        title: `Suspend ${org.name}?`,
        message: 'All of its users are signed out immediately and cannot sign in until you reactivate it. The reason is recorded in the audit log.',
        confirmLabel: 'Suspend organization',
        destructive: true,
        reasonLabel: 'Reason for suspension',
      })
      if (confirmed && reason) {
        suspendOrg({ id: org.id, reason }, { onSuccess: () => toast.success(`${org.name} suspended.`) })
      }
    },
    [confirm, suspendOrg],
  )

  const handleActivate = useCallback(
    async (org: PlatformOrganization) => {
      const { confirmed } = await confirm({
        title: `Reactivate ${org.name}?`,
        message: 'Its users will be able to sign in again.',
        confirmLabel: 'Reactivate',
      })
      if (confirmed) {
        activateOrg(org.id, { onSuccess: () => toast.success(`${org.name} reactivated.`) })
      }
    },
    [confirm, activateOrg],
  )

  const columns = useMemo<DataColumn<PlatformOrganization>[]>(
    () => [
      {
        id: 'name',
        header: 'Organization',
        sortKey: 'name',
        required: true,
        csv: (o) => o.name,
        renderCell: (o) => (
          <>
            <Typography variant="body2" sx={{ fontWeight: 600 }}>{o.name}</Typography>
            <Typography variant="caption" color="textSecondary">/{o.slug}</Typography>
          </>
        ),
      },
      { id: 'contact', header: 'Contact', hideBelow: 'md', csv: (o) => o.contactEmail, renderCell: (o) => o.contactEmail },
      { id: 'users', header: 'Users', align: 'right', csv: (o) => o.userCount, renderCell: (o) => o.userCount },
      {
        id: 'status',
        header: 'Status',
        csv: (o) => o.status,
        renderCell: (o) =>
          o.status === 'Suspended' ? (
            <Tooltip title={o.suspensionReason ?? 'Suspended'}>
              <span><StatusBadge label="Suspended" tone="error" /></span>
            </Tooltip>
          ) : (
            <StatusBadge label="Active" tone="success" />
          ),
      },
      { id: 'created', header: 'Created', sortKey: 'createdAt', hideBelow: 'lg', csv: (o) => o.createdAt, renderCell: (o) => formatDate(o.createdAt, timeZone) },
      {
        id: 'actions',
        header: '',
        align: 'right',
        required: true,
        renderCell: (o) =>
          o.status === 'Active' ? (
            <Button size="small" color="error" startIcon={<BlockIcon />} onClick={() => void handleSuspend(o)} disabled={suspending}>
              Suspend
            </Button>
          ) : (
            <Button size="small" startIcon={<RestoreIcon />} onClick={() => void handleActivate(o)} disabled={activating}>
              Reactivate
            </Button>
          ),
      },
    ],
    [timeZone, suspending, activating, handleSuspend, handleActivate],
  )

  return (
    <>
      <PageHeader
        title="Organizations"
        subtitle="Every customer organization on the platform. You manage their existence, never their data."
      />
      <DataTable
        ariaLabel="Organizations"
        columns={columns}
        rows={organizations.data?.items ?? []}
        getRowId={(o) => o.id}
        getRowLabel={(o) => o.name}
        loading={organizations.isPending}
        fetching={organizations.isFetching && !organizations.isPending}
        error={organizations.error}
        onRetry={() => void organizations.refetch()}
        page={table.page}
        pageSize={table.pageSize}
        totalCount={organizations.data?.totalCount ?? 0}
        onPageChange={(page) => table.update({ page })}
        onPageSizeChange={(pageSize) => table.update({ pageSize })}
        sortBy={table.sortBy}
        sortDirection={table.sortDirection}
        onSortChange={(sortBy, sortDirection) => table.update({ sortBy, sortDirection })}
        exportFileName="organizations.csv"
        toolbar={
          <FilterBar canReset={table.hasActiveFilters} onReset={table.clearFilters}>
            <SearchBox value={table.q} onChange={setSearch} placeholder="Name, address or email" label="Search organizations" />
            <TextField select label="Status" value={status ?? ''} onChange={(event) => table.update({ status: event.target.value })} fullWidth={false} sx={{ minWidth: 160 }}>
              <MenuItem value="">All</MenuItem>
              <MenuItem value="Active">Active</MenuItem>
              <MenuItem value="Suspended">Suspended</MenuItem>
            </TextField>
          </FilterBar>
        }
      />
    </>
  )
}
