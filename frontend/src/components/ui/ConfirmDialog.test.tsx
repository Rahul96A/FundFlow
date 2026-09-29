import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderHook, act } from '@testing-library/react'
import { vi } from 'vitest'
import { renderWithProviders } from '@/test/utils'
import { ConfirmDialog } from './ConfirmDialog'
import { useConfirm } from './confirmStore'

describe('ConfirmDialog', () => {
  it('asks, and reports the choice', async () => {
    const user = userEvent.setup()
    const onConfirm = vi.fn()
    const onCancel = vi.fn()
    renderWithProviders(
      <ConfirmDialog open title="Delete role?" message="This cannot be undone." confirmLabel="Delete" destructive onConfirm={onConfirm} onCancel={onCancel} />,
    )

    expect(screen.getByRole('dialog', { name: 'Delete role?' })).toBeInTheDocument()
    expect(screen.getByText('This cannot be undone.')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(onCancel).toHaveBeenCalledOnce()
    await user.click(screen.getByRole('button', { name: 'Delete' }))
    expect(onConfirm).toHaveBeenCalledOnce()
  })

  it('cannot be confirmed without a reason when one is required, and passes the trimmed reason', async () => {
    const user = userEvent.setup()
    const onConfirm = vi.fn()
    renderWithProviders(
      <ConfirmDialog open title="Suspend?" message="Why?" reasonLabel="Reason" confirmLabel="Suspend" onConfirm={onConfirm} onCancel={vi.fn()} />,
    )

    const confirm = screen.getByRole('button', { name: 'Suspend' })
    expect(confirm).toBeDisabled()
    await user.type(screen.getByRole('textbox', { name: /reason/i }), '  unpaid invoice  ')
    expect(confirm).toBeEnabled()
    await user.click(confirm)
    expect(onConfirm).toHaveBeenCalledWith('unpaid invoice')
  })

  it('blocks both buttons while a request is in flight', () => {
    renderWithProviders(<ConfirmDialog open title="T" message="M" loading onConfirm={vi.fn()} onCancel={vi.fn()} />)

    expect(screen.getByRole('button', { name: 'Confirm' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled()
  })
})

/** Renders nothing; it only gives `renderWithProviders` a tree to mount the confirm host into. */
function Harness() {
  return null
}

describe('useConfirm (imperative)', () => {
  it('resolves with the users decision and shows the dialog through the host', async () => {
    const user = userEvent.setup()
    renderWithProviders(<Harness />)
    const { result } = renderHook(() => useConfirm())

    let pending!: Promise<{ confirmed: boolean; reason?: string }>
    act(() => {
      pending = result.current({ title: 'Deactivate Ada?', message: 'They will be signed out.', confirmLabel: 'Deactivate', destructive: true })
    })
    expect(await screen.findByRole('dialog', { name: 'Deactivate Ada?' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Deactivate' }))

    await expect(pending).resolves.toEqual({ confirmed: true, reason: undefined })
  })

  it('resolves confirmed=false when cancelled, and a second question cancels the first', async () => {
    const user = userEvent.setup()
    renderWithProviders(<Harness />)
    const { result } = renderHook(() => useConfirm())

    let first!: Promise<{ confirmed: boolean }>
    let second!: Promise<{ confirmed: boolean }>
    act(() => {
      first = result.current({ title: 'First?', message: '1' })
      second = result.current({ title: 'Second?', message: '2' })
    })
    await expect(first).resolves.toMatchObject({ confirmed: false })
    expect(await screen.findByRole('dialog', { name: 'Second?' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Cancel' }))
    await expect(second).resolves.toMatchObject({ confirmed: false })
  })
})
