import { act, renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { MemoryRouter } from 'react-router'
import { useTableParams } from './useTableParams'

function setup(initial = '/users') {
  const wrapper = ({ children }: { children: ReactNode }) => <MemoryRouter initialEntries={[initial]}>{children}</MemoryRouter>
  return renderHook(() => useTableParams(['status'] as const, { sortBy: 'createdAt', sortDirection: 'desc' }), { wrapper })
}

describe('useTableParams', () => {
  it('reads defaults when the URL is bare', () => {
    const { result } = setup()

    expect(result.current).toMatchObject({ page: 1, pageSize: 25, sortBy: 'createdAt', sortDirection: 'desc', q: '', hasActiveFilters: false })
    expect(result.current.filters).toEqual({ status: '' })
  })

  it('reads state from the URL so a shared link reproduces the view', () => {
    const { result } = setup('/users?page=3&pageSize=50&sortBy=email&sortDirection=asc&q=ada&status=Locked')

    expect(result.current).toMatchObject({ page: 3, pageSize: 50, sortBy: 'email', sortDirection: 'asc', q: 'ada', hasActiveFilters: true })
    expect(result.current.filters.status).toBe('Locked')
  })

  it('sanitises hostile or nonsense parameters', () => {
    const { result } = setup('/users?page=-4&pageSize=9999&sortDirection=sideways')

    expect(result.current.page).toBe(1)
    expect(result.current.pageSize).toBe(25)
    expect(result.current.sortDirection).toBe('desc')
  })

  it('returns to page 1 when anything other than the page changes', () => {
    const { result } = setup('/users?page=4')

    act(() => result.current.update({ q: 'grace' }))

    expect(result.current.q).toBe('grace')
    expect(result.current.page).toBe(1)
  })

  it('keeps the page when the page itself is what changed', () => {
    const { result } = setup()

    act(() => result.current.update({ page: 5 }))

    expect(result.current.page).toBe(5)
  })

  it('removes a parameter when it is set to an empty value', () => {
    const { result } = setup('/users?status=Locked')

    act(() => result.current.update({ status: '' }))

    expect(result.current.filters.status).toBe('')
    expect(result.current.hasActiveFilters).toBe(false)
  })

  it('clears search and filters but keeps sorting and page size', () => {
    const { result } = setup('/users?q=ada&status=Locked&sortBy=email&pageSize=50&page=2')

    act(() => result.current.clearFilters())

    expect(result.current).toMatchObject({ q: '', page: 1, pageSize: 50, sortBy: 'email', hasActiveFilters: false })
  })
})
