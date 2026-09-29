import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import { renderWithProviders } from '@/test/utils'
import * as csv from '@/shared/utils/csv'
import { DataTable, type DataColumn, type DataTableProps } from './DataTable'

interface Row {
  id: string
  name: string
  email: string
}

const rows: Row[] = [
  { id: '1', name: 'Ada Lovelace', email: 'ada@hope.org' },
  { id: '2', name: 'Grace Hopper', email: 'grace@hope.org' },
  { id: '3', name: 'Katherine Johnson', email: 'kj@hope.org' },
]

const columns: DataColumn<Row>[] = [
  { id: 'name', header: 'Name', sortKey: 'name', required: true, renderCell: (r) => r.name, csv: (r) => r.name },
  { id: 'email', header: 'Email', sortKey: 'email', renderCell: (r) => r.email, csv: (r) => r.email },
  { id: 'secret', header: 'Internal ID', hiddenByDefault: true, renderCell: (r) => `id-${r.id}`, csv: (r) => r.id },
]

function renderTable(overrides: Partial<DataTableProps<Row>> = {}) {
  const props: DataTableProps<Row> = {
    ariaLabel: 'People',
    columns,
    rows,
    getRowId: (r) => r.id,
    getRowLabel: (r) => r.name,
    page: 1,
    pageSize: 25,
    totalCount: 3,
    onPageChange: vi.fn(),
    onPageSizeChange: vi.fn(),
    ...overrides,
  }
  renderWithProviders(<DataTable {...props} />)
  return props
}

describe('DataTable', () => {
  it('renders an accessible table with the visible columns only', () => {
    renderTable()

    expect(screen.getByRole('table', { name: 'People' })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: /name/i })).toBeInTheDocument()
    expect(screen.queryByRole('columnheader', { name: /internal id/i })).not.toBeInTheDocument()
    expect(screen.getByText('Grace Hopper')).toBeInTheDocument()
    expect(screen.queryByText('id-1')).not.toBeInTheDocument()
  })

  it('lets users reveal a hidden column from the column menu, but never a required one', async () => {
    const user = userEvent.setup()
    renderTable()

    await user.click(screen.getByRole('button', { name: /choose columns/i }))
    expect(screen.getByRole('menuitem', { name: 'Name' })).toHaveAttribute('aria-disabled', 'true')
    await user.click(screen.getByRole('menuitem', { name: 'Internal ID' }))
    await user.keyboard('{Escape}') // an open menu hides the table from assistive technology, so close it first

    expect(await screen.findByRole('columnheader', { name: /internal id/i })).toBeInTheDocument()
    expect(screen.getByText('id-1')).toBeInTheDocument()
  })

  it('reports sort changes: ascending first, then toggling direction', async () => {
    const user = userEvent.setup()
    const onSortChange = vi.fn()
    renderTable({ sortBy: 'name', sortDirection: 'asc', onSortChange })

    expect(screen.getByRole('columnheader', { name: /name/i })).toHaveAttribute('aria-sort', 'ascending')
    await user.click(screen.getByRole('button', { name: /^name/i }))
    expect(onSortChange).toHaveBeenLastCalledWith('name', 'desc')

    await user.click(screen.getByRole('button', { name: /^email/i }))
    expect(onSortChange).toHaveBeenLastCalledWith('email', 'asc')
  })

  it('shows skeleton rows while the first page loads and never shows the empty message meanwhile', () => {
    renderTable({ rows: [], loading: true, totalCount: 0 })

    expect(screen.getByRole('table', { name: 'People' })).toHaveAttribute('aria-busy', 'true')
    expect(screen.queryByText('Nothing to show')).not.toBeInTheDocument()
  })

  it('shows the empty state when there is nothing to list', () => {
    renderTable({ rows: [], totalCount: 0 })

    expect(screen.getByText('Nothing to show')).toBeInTheDocument()
  })

  it('shows a custom empty state', () => {
    renderTable({ rows: [], totalCount: 0, emptyState: <p>No people yet</p> })

    expect(screen.getByText('No people yet')).toBeInTheDocument()
  })

  it('shows an error with a working retry when the load failed', async () => {
    const user = userEvent.setup()
    const onRetry = vi.fn()
    renderTable({ rows: [], error: new Error('The server is having a moment.'), onRetry })

    expect(screen.getByRole('alert')).toHaveTextContent('The server is having a moment.')
    await user.click(screen.getByRole('button', { name: /try again/i }))
    expect(onRetry).toHaveBeenCalledOnce()
  })

  it('keeps showing rows (with a progress bar) when a background refresh fails', () => {
    renderTable({ error: new Error('refresh failed'), fetching: true })

    expect(screen.getByText('Ada Lovelace')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.getByRole('progressbar', { name: /refreshing/i })).toBeInTheDocument()
  })

  it('pages: 1-based page numbers out, 0-based in', async () => {
    const user = userEvent.setup()
    const props = renderTable({ page: 2, pageSize: 10, totalCount: 35 })

    expect(screen.getByText('11–20 of 35')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /next page/i }))
    expect(props.onPageChange).toHaveBeenLastCalledWith(3)
    await user.click(screen.getByRole('button', { name: /previous page/i }))
    expect(props.onPageChange).toHaveBeenLastCalledWith(1)
  })

  describe('selection and bulk actions', () => {
    it('selects rows, offers bulk actions for exactly those rows, and clears', async () => {
      const user = userEvent.setup()
      const onSelectionChange = vi.fn()
      const renderBulkActions = vi.fn(() => <button>Do it</button>)
      renderTable({ selectable: true, selectedIds: [], onSelectionChange, renderBulkActions })

      await user.click(screen.getByRole('checkbox', { name: 'Select Grace Hopper' }))
      expect(onSelectionChange).toHaveBeenLastCalledWith(['2'])
      expect(screen.queryByRole('region', { name: /bulk actions/i })).not.toBeInTheDocument()
    })

    it('shows the bulk bar when rows are selected and clears the selection on request', async () => {
      const user = userEvent.setup()
      const onSelectionChange = vi.fn()
      renderTable({ selectable: true, selectedIds: ['1', '3'], onSelectionChange, renderBulkActions: (ids) => <span>acting on {ids.join('+')}</span> })

      const bar = screen.getByRole('region', { name: /bulk actions/i })
      expect(within(bar).getByText('2 selected')).toBeInTheDocument()
      expect(within(bar).getByText('acting on 1+3')).toBeInTheDocument()
      await user.click(within(bar).getByRole('button', { name: /clear selection/i }))
      expect(onSelectionChange).toHaveBeenLastCalledWith([])
    })

    it('select-all covers the current page and shows an indeterminate state for a partial selection', async () => {
      const user = userEvent.setup()
      const onSelectionChange = vi.fn()
      renderTable({ selectable: true, selectedIds: ['1'], onSelectionChange })

      const all = screen.getByRole('checkbox', { name: /select all rows/i })
      expect(all).toHaveAttribute('data-indeterminate', 'true')
      await user.click(all)
      expect(onSelectionChange).toHaveBeenLastCalledWith(['1', '2', '3'])
    })

    it('clicking a checkbox does not trigger row navigation', async () => {
      const user = userEvent.setup()
      const onRowClick = vi.fn()
      renderTable({ selectable: true, selectedIds: [], onSelectionChange: vi.fn(), onRowClick })

      await user.click(screen.getByRole('checkbox', { name: 'Select Ada Lovelace' }))
      expect(onRowClick).not.toHaveBeenCalled()
      await user.click(screen.getByText('Ada Lovelace'))
      expect(onRowClick).toHaveBeenCalledWith(rows[0])
    })
  })

  describe('export', () => {
    it('exports the visible columns of the rows on screen as CSV', async () => {
      const user = userEvent.setup()
      const download = vi.spyOn(csv, 'downloadCsv').mockImplementation(() => undefined)
      renderTable({ exportFileName: 'people.csv' })

      await user.click(screen.getByRole('button', { name: /export/i }))

      expect(download).toHaveBeenCalledOnce()
      const [filename, content] = download.mock.calls[0]!
      expect(filename).toBe('people.csv')
      expect(content).toBe('Name,Email\r\nAda Lovelace,ada@hope.org\r\nGrace Hopper,grace@hope.org\r\nKatherine Johnson,kj@hope.org')
    })

    it('has no export button unless a file name is provided', () => {
      renderTable()

      expect(screen.queryByRole('button', { name: /export/i })).not.toBeInTheDocument()
    })

    it('disables export when there is nothing to export', () => {
      renderTable({ exportFileName: 'people.csv', rows: [], totalCount: 0 })

      expect(screen.getByRole('button', { name: /export/i })).toBeDisabled()
    })
  })

  it('renders the toolbar content', () => {
    renderTable({ toolbar: <input aria-label="Search people" /> })

    expect(screen.getByRole('textbox', { name: 'Search people' })).toBeInTheDocument()
  })
})
