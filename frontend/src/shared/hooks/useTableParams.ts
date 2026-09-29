import { useCallback, useMemo } from 'react'
import { useSearchParams } from 'react-router'
import type { SortDirection } from '@/shared/types/api'

export const PAGE_SIZES = [10, 25, 50, 100] as const
const DEFAULT_PAGE_SIZE = 25

interface Defaults {
  sortBy?: string
  sortDirection?: SortDirection
  pageSize?: number
}

function positiveInt(raw: string | null, fallback: number): number {
  const parsed = Number.parseInt(raw ?? '', 10)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
}

/**
 * Table state (page, size, sort, search and filters) lives in the URL, so a filtered view can be bookmarked or
 * shared, survives a refresh, and works with the back button. Changing anything but the page returns to page 1.
 */
export function useTableParams<TFilter extends string = never>(filterKeys: readonly TFilter[] = [], defaults: Defaults = {}) {
  const [search, setSearch] = useSearchParams()
  const filterKeyList = filterKeys.join(',')

  const pageSizeRaw = positiveInt(search.get('pageSize'), defaults.pageSize ?? DEFAULT_PAGE_SIZE)
  const pageSize = (PAGE_SIZES as readonly number[]).includes(pageSizeRaw) ? pageSizeRaw : (defaults.pageSize ?? DEFAULT_PAGE_SIZE)
  const page = positiveInt(search.get('page'), 1)
  const sortBy = search.get('sortBy') ?? defaults.sortBy
  const directionRaw = search.get('sortDirection')
  const sortDirection: SortDirection =
    directionRaw === 'asc' || directionRaw === 'desc' ? directionRaw : (defaults.sortDirection ?? 'asc')
  const q = search.get('q') ?? ''

  const filters = useMemo(
    () => Object.fromEntries(filterKeyList.split(',').filter(Boolean).map((key) => [key, search.get(key) ?? ''])) as Record<TFilter, string>,
    [filterKeyList, search],
  )

  const update = useCallback(
    (patch: Record<string, string | number | null | undefined>) => {
      setSearch(
        (previous) => {
          const next = new URLSearchParams(previous)
          for (const [key, value] of Object.entries(patch)) {
            if (value === undefined || value === null || value === '') {
              next.delete(key)
            } else {
              next.set(key, String(value))
            }
          }
          if (!('page' in patch)) {
            next.delete('page')
          }
          return next
        },
        { replace: true },
      )
    },
    [setSearch],
  )

  const hasActiveFilters = q !== '' || Object.values<string>(filters).some((value) => value !== '')

  const clearFilters = useCallback(() => {
    setSearch(
      (previous) => {
        const next = new URLSearchParams(previous)
        next.delete('q')
        next.delete('page')
        for (const key of filterKeyList.split(',').filter(Boolean)) {
          next.delete(key)
        }
        return next
      },
      { replace: true },
    )
  }, [filterKeyList, setSearch])

  return { page, pageSize, sortBy, sortDirection, q, filters, hasActiveFilters, update, clearFilters }
}
