import Box from '@mui/material/Box'
import Divider from '@mui/material/Divider'
import Stack from '@mui/material/Stack'
import Table from '@mui/material/Table'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import Typography from '@mui/material/Typography'
import type { ReactNode } from 'react'
import { useAuthStore } from '@/app/store/authStore'
import { Drawer } from '@/components/ui/Drawer'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { describeUserAgent, formatDateTime } from '@/shared/utils/format'
import type { AuditLogEntry } from '../types/audit.types'
import { describeAction } from '../utils/describeAction'

const show = (value: unknown): string => {
  if (value === null || value === undefined) {
    return '—'
  }
  return typeof value === 'object' ? JSON.stringify(value) : String(value)
}

/** Who did what to what, from where; plus a field-by-field before/after when the entry carries values. */
export function AuditEntryDrawer({ entry, onClose }: { entry: AuditLogEntry | null; onClose: () => void }) {
  const timeZone = useAuthStore((s) => s.user?.organization?.timeZoneId)
  const info = entry ? describeAction(entry.action) : null

  const keys = entry
    ? [...new Set([...Object.keys(entry.oldValues ?? {}), ...Object.keys(entry.newValues ?? {})])].toSorted()
    : []

  return (
    <Drawer open={entry !== null} onClose={onClose} title={info?.label ?? 'Audit entry'} subtitle={entry?.action} width={560}>
      {entry && info ? (
        <Stack spacing={2.5} divider={<Divider flexItem />}>
          <Stack spacing={1}>
            <Row label="When" value={formatDateTime(entry.timestamp, timeZone)} />
            <Row label="Who" value={entry.userEmail ?? 'System'} />
            <Row label="What" value={<StatusBadge label={entry.action} tone={info.tone} />} />
            <Row label="Record" value={`${entry.entityType}${entry.entityId ? ` · ${entry.entityId}` : ''}`} />
            <Row label="IP address" value={entry.ipAddress ?? '—'} />
            <Row label="Device" value={describeUserAgent(entry.userAgent)} />
            <Row label="Correlation ID" value={entry.correlationId ?? '—'} />
          </Stack>

          {keys.length > 0 ? (
            <Box>
              <Typography variant="h5" component="h3" sx={{ mb: 1 }}>Changes</Typography>
              <Table size="small" aria-label="Changed values">
                <TableHead>
                  <TableRow>
                    <TableCell>Field</TableCell>
                    <TableCell>Before</TableCell>
                    <TableCell>After</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {keys.map((key) => (
                    <TableRow key={key}>
                      <TableCell sx={{ fontWeight: 600 }}>{key}</TableCell>
                      <TableCell sx={{ wordBreak: 'break-word', color: 'text.secondary' }}>{show(entry.oldValues?.[key])}</TableCell>
                      <TableCell sx={{ wordBreak: 'break-word' }}>{show(entry.newValues?.[key])}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Box>
          ) : (
            <Typography variant="body2" color="textSecondary">This entry has no recorded field changes.</Typography>
          )}
          <Typography variant="caption" color="textSecondary">
            Audit entries are append-only. Secrets such as passwords and tokens are never recorded.
          </Typography>
        </Stack>
      ) : null}
    </Drawer>
  )
}

function Row({ label, value }: { label: string; value: ReactNode }) {
  return (
    <Stack direction="row" spacing={2} sx={{ alignItems: 'baseline' }}>
      <Typography variant="body2" color="textSecondary" sx={{ width: 120, flexShrink: 0 }}>{label}</Typography>
      <Typography variant="body2" component="div" sx={{ wordBreak: 'break-word' }}>{value}</Typography>
    </Stack>
  )
}
