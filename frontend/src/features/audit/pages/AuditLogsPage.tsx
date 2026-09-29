import MenuItem from '@mui/material/MenuItem'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { useCallback, useMemo, useState } from 'react'
import { useAuthStore } from '@/app/store/authStore'
import { DataTable, type DataColumn } from '@/components/tables/DataTable'
import { FilterBar } from '@/components/tables/FilterBar'
import { PageHeader } from '@/components/ui/PageHeader'
import { DateRangePicker } from '@/components/ui/DateRangePicker'
import { SearchBox } from '@/components/ui/SearchBox'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { EmptyState } from '@/components/ui/States'
import { useTableParams } from '@/shared/hooks/useTableParams'
import { formatDateTime } from '@/shared/utils/format'
import { AuditEntryDrawer } from '../components/AuditEntryDrawer'
import { useAuditLogs } from '../hooks/useAuditLogs'
import type { AuditLogEntry } from '../types/audit.types'
import { auditActionFilters, describeAction } from '../utils/describeAction'

export default function AuditLogsPage() {
  const timeZone = useAuthStore((s) => s.user?.organization?.timeZoneId)
  const table = useTableParams(['action', 'from', 'to'] as const, { sortBy: 'timestamp', sortDirection: 'desc' })
  const [openEntry, setOpenEntry] = useState<AuditLogEntry | null>(null)

  const logs = useAuditLogs({
    page: table.page,
    pageSize: table.pageSize,
    sortBy: table.sortBy,
    sortDirection: table.sortDirection,
    search: table.q || undefined,
    action: table.filters.action || undefined,
    from: table.filters.from || undefined,
    to: table.filters.to || undefined,
  })

  const setSearch = useCallback((value: string) => table.update({ q: value }), [table])

  const columns = useMemo<DataColumn<AuditLogEntry>[]>(
    () => [
      {
        id: 'timestamp',
        header: 'When',
        sortKey: 'timestamp',
        required: true,
        csv: (e) => e.timestamp,
        renderCell: (e) => (
          <Typography variant="body2" sx={{ whiteSpace: 'nowrap' }}>
            {formatDateTime(e.timestamp, timeZone)}
          </Typography>
        ),
      },
      {
        id: 'action',
        header: 'Action',
        sortKey: 'action',
        csv: (e) => e.action,
        renderCell: (e) => {
          const info = describeAction(e.action)
          return <StatusBadge label={info.label} tone={info.tone} />
        },
      },
      {
        id: 'actor',
        header: 'Actor',
        sortKey: 'userEmail',
        hideBelow: 'sm',
        csv: (e) => e.userEmail ?? 'System',
        renderCell: (e) => e.userEmail ?? <Typography variant="body2" color="textSecondary">System</Typography>,
      },
      {
        id: 'entity',
        header: 'Record',
        sortKey: 'entityType',
        hideBelow: 'md',
        csv: (e) => `${e.entityType} ${e.entityId ?? ''}`.trim(),
        renderCell: (e) => (
          <>
            {e.entityType}
            {e.entityId ? (
              <Typography component="span" variant="caption" color="textSecondary" sx={{ ml: 1 }}>
                {e.entityId.slice(0, 8)}
              </Typography>
            ) : null}
          </>
        ),
      },
      { id: 'ip', header: 'IP address', hideBelow: 'lg', hiddenByDefault: true, csv: (e) => e.ipAddress ?? '', renderCell: (e) => e.ipAddress ?? '—' },
    ],
    [timeZone],
  )

  return (
    <>
      <PageHeader title="Audit log" subtitle="An append-only record of sensitive activity in your organization." />
      <DataTable
        ariaLabel="Audit log"
        columns={columns}
        rows={logs.data?.items ?? []}
        getRowId={(e) => e.id}
        getRowLabel={(e) => `${e.action} at ${e.timestamp}`}
        loading={logs.isPending}
        fetching={logs.isFetching && !logs.isPending}
        error={logs.error}
        onRetry={() => void logs.refetch()}
        page={table.page}
        pageSize={table.pageSize}
        totalCount={logs.data?.totalCount ?? 0}
        onPageChange={(page) => table.update({ page })}
        onPageSizeChange={(pageSize) => table.update({ pageSize })}
        sortBy={table.sortBy}
        sortDirection={table.sortDirection}
        onSortChange={(sortBy, sortDirection) => table.update({ sortBy, sortDirection })}
        onRowClick={setOpenEntry}
        exportFileName="audit-log.csv"
        toolbar={
          <FilterBar canReset={table.hasActiveFilters} onReset={table.clearFilters}>
            <SearchBox value={table.q} onChange={setSearch} placeholder="Email, action or record" label="Search audit log" />
            <TextField
              select
              label="Activity"
              value={table.filters.action}
              onChange={(event) => table.update({ action: event.target.value })}
              fullWidth={false} sx={{ minWidth: 200 }}
            >
              {auditActionFilters.map((option) => (
                <MenuItem key={option.value} value={option.value}>
                  {option.label}
                </MenuItem>
              ))}
            </TextField>
            <DateRangePicker
              value={{ from: table.filters.from || null, to: table.filters.to || null }}
              onChange={(range) => table.update({ from: range.from, to: range.to })}
            />
          </FilterBar>
        }
        emptyState={
          <EmptyState
            title={table.hasActiveFilters ? 'No entries match these filters' : 'No activity recorded yet'}
            description={table.hasActiveFilters ? 'Widen the date range or clear the filters.' : 'Sign-ins, invitations and changes will appear here.'}
          />
        }
      />
      <AuditEntryDrawer entry={openEntry} onClose={() => setOpenEntry(null)} />
    </>
  )
}
