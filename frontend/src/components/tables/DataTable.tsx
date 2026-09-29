import DownloadIcon from '@mui/icons-material/Download'
import ViewColumnIcon from '@mui/icons-material/ViewColumn'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Checkbox from '@mui/material/Checkbox'
import Divider from '@mui/material/Divider'
import IconButton from '@mui/material/IconButton'
import LinearProgress from '@mui/material/LinearProgress'
import ListItemText from '@mui/material/ListItemText'
import Menu from '@mui/material/Menu'
import MenuItem from '@mui/material/MenuItem'
import Paper from '@mui/material/Paper'
import Skeleton from '@mui/material/Skeleton'
import Stack from '@mui/material/Stack'
import Table from '@mui/material/Table'
import TableBody from '@mui/material/TableBody'
import TableCell, { type TableCellProps } from '@mui/material/TableCell'
import TableContainer from '@mui/material/TableContainer'
import TableHead from '@mui/material/TableHead'
import TablePagination from '@mui/material/TablePagination'
import TableRow from '@mui/material/TableRow'
import TableSortLabel from '@mui/material/TableSortLabel'
import Tooltip from '@mui/material/Tooltip'
import Typography from '@mui/material/Typography'
import { useMemo, useState, type ReactNode } from 'react'
import { EmptyState, ErrorState } from '@/components/ui/States'
import type { SortDirection } from '@/shared/types/api'
import { downloadCsv, toCsv, type CsvCell } from '@/shared/utils/csv'

export interface DataColumn<T> {
  id: string
  header: string
  renderCell: (row: T) => ReactNode
  /** Server-side sort field. When set the header is a sort control. */
  sortKey?: string
  align?: TableCellProps['align']
  width?: number | string
  /** Hide the column below this breakpoint (responsive behaviour for narrow screens). */
  hideBelow?: 'sm' | 'md' | 'lg'
  /** Starts hidden; users can reveal it from the column menu. */
  hiddenByDefault?: boolean
  /** Cannot be hidden from the column menu (the identifying column). */
  required?: boolean
  /** Plain-text value for CSV export. Columns without it are left out of the export. */
  csv?: (row: T) => CsvCell
}

export interface DataTableProps<T> {
  columns: DataColumn<T>[]
  rows: T[]
  getRowId: (row: T) => string
  /** Accessible name for a row (used by the selection checkbox and row buttons). */
  getRowLabel?: (row: T) => string
  ariaLabel: string

  loading?: boolean
  /** True while refetching with rows already on screen; shows a thin progress bar instead of skeletons. */
  fetching?: boolean
  error?: unknown
  onRetry?: () => void

  page: number
  pageSize: number
  totalCount: number
  onPageChange: (page: number) => void
  onPageSizeChange: (pageSize: number) => void
  pageSizeOptions?: number[]

  sortBy?: string
  sortDirection?: SortDirection
  onSortChange?: (sortBy: string, direction: SortDirection) => void

  selectable?: boolean
  selectedIds?: string[]
  onSelectionChange?: (ids: string[]) => void
  /** Rendered next to "N selected" when at least one row is selected. */
  renderBulkActions?: (selectedIds: string[]) => ReactNode

  /** Search box, filters, primary actions. */
  toolbar?: ReactNode
  emptyState?: ReactNode
  onRowClick?: (row: T) => void
  /** Enables the Export button; the file contains the rows currently loaded (this page). */
  exportFileName?: string
}

const breakpointDisplay = {
  sm: { xs: 'none', sm: 'table-cell' },
  md: { xs: 'none', md: 'table-cell' },
  lg: { xs: 'none', lg: 'table-cell' },
} as const

export function DataTable<T>({
  columns,
  rows,
  getRowId,
  getRowLabel,
  ariaLabel,
  loading = false,
  fetching = false,
  error,
  onRetry,
  page,
  pageSize,
  totalCount,
  onPageChange,
  onPageSizeChange,
  pageSizeOptions = [10, 25, 50, 100],
  sortBy,
  sortDirection = 'asc',
  onSortChange,
  selectable = false,
  selectedIds = [],
  onSelectionChange,
  renderBulkActions,
  toolbar,
  emptyState,
  onRowClick,
  exportFileName,
}: DataTableProps<T>) {
  const [hidden, setHidden] = useState<Set<string>>(
    () => new Set(columns.filter((c) => c.hiddenByDefault).map((c) => c.id)),
  )
  const [columnMenu, setColumnMenu] = useState<HTMLElement | null>(null)

  const visibleColumns = useMemo(() => columns.filter((c) => !hidden.has(c.id)), [columns, hidden])
  const pageIds = useMemo(() => rows.map(getRowId), [rows, getRowId])
  const selectedOnPage = pageIds.filter((id) => selectedIds.includes(id))
  const allSelected = pageIds.length > 0 && selectedOnPage.length === pageIds.length
  const someSelected = selectedOnPage.length > 0 && !allSelected
  const colSpan = visibleColumns.length + (selectable ? 1 : 0)
  const exportable = exportFileName !== undefined && columns.some((c) => c.csv)
  const showSkeleton = loading && rows.length === 0

  function toggleAll() {
    if (!onSelectionChange) {
      return
    }
    onSelectionChange(allSelected ? selectedIds.filter((id) => !pageIds.includes(id)) : [...new Set([...selectedIds, ...pageIds])])
  }

  function toggleOne(id: string) {
    onSelectionChange?.(selectedIds.includes(id) ? selectedIds.filter((s) => s !== id) : [...selectedIds, id])
  }

  function handleExport() {
    const exportColumns = visibleColumns.filter((c) => c.csv)
    const csv = toCsv(
      exportColumns.map((c) => c.header),
      rows.map((row) => exportColumns.map((c) => c.csv!(row))),
    )
    downloadCsv(exportFileName!, csv)
  }

  return (
    <Paper variant="outlined" sx={{ overflow: 'hidden' }}>
      {toolbar || exportable || columns.some((c) => !c.required) ? (
        <Stack
          direction={{ xs: 'column', md: 'row' }}
          spacing={1.5}
          sx={{ alignItems: { xs: 'stretch', md: 'center' }, p: 2 }}
        >
          <Box sx={{ flex: 1, minWidth: 0 }}>{toolbar}</Box>
          <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', justifyContent: 'flex-end' }}>
            {exportable ? (
              <Tooltip title="Export the rows on this page as CSV">
                <span>
                  <Button
                    size="small"
                    color="inherit"
                    startIcon={<DownloadIcon />}
                    onClick={handleExport}
                    disabled={rows.length === 0}
                  >
                    Export
                  </Button>
                </span>
              </Tooltip>
            ) : null}
            <Tooltip title="Show or hide columns">
              <IconButton aria-label="Choose columns" onClick={(event) => setColumnMenu(event.currentTarget)}>
                <ViewColumnIcon />
              </IconButton>
            </Tooltip>
            <Menu anchorEl={columnMenu} open={Boolean(columnMenu)} onClose={() => setColumnMenu(null)}>
              {columns.map((column) => (
                <MenuItem
                  key={column.id}
                  dense
                  disabled={column.required}
                  onClick={() =>
                    setHidden((current) => {
                      const next = new Set(current)
                      if (next.has(column.id)) {
                        next.delete(column.id)
                      } else {
                        next.add(column.id)
                      }
                      return next
                    })
                  }
                >
                  <Checkbox edge="start" size="small" checked={!hidden.has(column.id)} tabIndex={-1} disableRipple />
                  <ListItemText primary={column.header} />
                </MenuItem>
              ))}
            </Menu>
          </Stack>
        </Stack>
      ) : null}

      {selectable && selectedIds.length > 0 ? (
        <Stack
          direction="row"
          spacing={2}
          sx={{ alignItems: 'center', px: 2, py: 1, bgcolor: 'action.selected' }}
          role="region"
          aria-label="Bulk actions"
        >
          <Typography variant="subtitle2">{selectedIds.length} selected</Typography>
          {renderBulkActions?.(selectedIds)}
          <Button size="small" color="inherit" onClick={() => onSelectionChange?.([])}>
            Clear selection
          </Button>
        </Stack>
      ) : null}

      {fetching ? <LinearProgress aria-label="Refreshing" /> : <Box sx={{ height: 4 }} />}
      <Divider />

      {error && rows.length === 0 ? (
        <ErrorState error={error} onRetry={onRetry} />
      ) : (
        <TableContainer>
          <Table size="small" aria-label={ariaLabel} aria-busy={loading || fetching} stickyHeader>
            <TableHead>
              <TableRow>
                {selectable ? (
                  <TableCell padding="checkbox">
                    <Checkbox
                      size="small"
                      checked={allSelected}
                      indeterminate={someSelected}
                      onChange={toggleAll}
                      disabled={rows.length === 0}
                      slotProps={{ input: { 'aria-label': 'Select all rows on this page' } }}
                    />
                  </TableCell>
                ) : null}
                {visibleColumns.map((column) => (
                  <TableCell
                    key={column.id}
                    align={column.align}
                    sx={{ width: column.width, display: column.hideBelow ? breakpointDisplay[column.hideBelow] : undefined }}
                    sortDirection={sortBy === column.sortKey ? sortDirection : false}
                    aria-sort={
                      column.sortKey && sortBy === column.sortKey
                        ? sortDirection === 'asc'
                          ? 'ascending'
                          : 'descending'
                        : undefined
                    }
                  >
                    {column.sortKey && onSortChange ? (
                      <TableSortLabel
                        active={sortBy === column.sortKey}
                        direction={sortBy === column.sortKey ? sortDirection : 'asc'}
                        onClick={() =>
                          onSortChange(
                            column.sortKey!,
                            sortBy === column.sortKey && sortDirection === 'asc' ? 'desc' : 'asc',
                          )
                        }
                      >
                        {column.header}
                      </TableSortLabel>
                    ) : (
                      column.header
                    )}
                  </TableCell>
                ))}
              </TableRow>
            </TableHead>
            <TableBody>
              {showSkeleton
                ? Array.from({ length: Math.min(pageSize, 6) }, (_, i) => (
                    <TableRow key={`skeleton-${i}`}>
                      {selectable ? (
                        <TableCell padding="checkbox">
                          <Skeleton variant="rounded" width={18} height={18} sx={{ mx: 1.5 }} />
                        </TableCell>
                      ) : null}
                      {visibleColumns.map((column) => (
                        <TableCell
                          key={column.id}
                          sx={{ display: column.hideBelow ? breakpointDisplay[column.hideBelow] : undefined }}
                        >
                          <Skeleton variant="text" />
                        </TableCell>
                      ))}
                    </TableRow>
                  ))
                : null}

              {!showSkeleton && rows.length === 0 ? (
                <TableRow>
                  <TableCell colSpan={colSpan} sx={{ border: 0 }}>
                    {emptyState ?? <EmptyState title="Nothing to show" description="No records match the current filters." />}
                  </TableCell>
                </TableRow>
              ) : null}

              {!showSkeleton
                ? rows.map((row) => {
                    const id = getRowId(row)
                    const selected = selectedIds.includes(id)
                    const label = getRowLabel?.(row) ?? id
                    return (
                      <TableRow
                        key={id}
                        hover
                        selected={selected}
                        onClick={onRowClick ? () => onRowClick(row) : undefined}
                        sx={{ cursor: onRowClick ? 'pointer' : undefined }}
                      >
                        {selectable ? (
                          <TableCell padding="checkbox" onClick={(event) => event.stopPropagation()}>
                            <Checkbox
                              size="small"
                              checked={selected}
                              onChange={() => toggleOne(id)}
                              slotProps={{ input: { 'aria-label': `Select ${label}` } }}
                            />
                          </TableCell>
                        ) : null}
                        {visibleColumns.map((column) => (
                          <TableCell
                            key={column.id}
                            align={column.align}
                            sx={{ display: column.hideBelow ? breakpointDisplay[column.hideBelow] : undefined }}
                          >
                            {column.renderCell(row)}
                          </TableCell>
                        ))}
                      </TableRow>
                    )
                  })
                : null}
            </TableBody>
          </Table>
        </TableContainer>
      )}

      <Divider />
      <TablePagination
        component="div"
        count={totalCount}
        page={Math.max(0, page - 1)}
        rowsPerPage={pageSize}
        rowsPerPageOptions={pageSizeOptions}
        onPageChange={(_event, zeroBased) => onPageChange(zeroBased + 1)}
        onRowsPerPageChange={(event) => onPageSizeChange(Number(event.target.value))}
        labelRowsPerPage="Rows"
      />
    </Paper>
  )
}
