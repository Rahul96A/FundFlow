export type SortDirection = 'asc' | 'desc'

/** Standard envelope returned by every list endpoint. */
export interface Paged<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

/** RFC 7807 body. `code` is the machine-readable reason; `errors` maps field name to messages. */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  code?: string
  errors?: Record<string, string[]>
  traceId?: string
  correlationId?: string
}

export interface TokenResponse {
  accessToken: string
  tokenType: string
  expiresInSeconds: number
  expiresAt: string
}

/** Query parameters shared by every paged list. */
export interface ListParams {
  page: number
  pageSize: number
  sortBy?: string
  sortDirection?: SortDirection
  search?: string
}
