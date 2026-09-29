import axios from 'axios'
import type { ProblemDetails } from '@/shared/types/api'

/** A failed API call, normalised from an RFC 7807 response (or a network failure). */
export class ApiError extends Error {
  readonly status: number
  readonly code: string | undefined
  readonly title: string | undefined
  readonly fieldErrors: Record<string, string[]>
  readonly traceId: string | undefined

  constructor(init: {
    status: number
    message: string
    code?: string
    title?: string
    fieldErrors?: Record<string, string[]>
    traceId?: string
  }) {
    super(init.message)
    this.name = 'ApiError'
    this.status = init.status
    this.code = init.code
    this.title = init.title
    this.fieldErrors = init.fieldErrors ?? {}
    this.traceId = init.traceId
  }

  get isNetworkError(): boolean {
    return this.status === 0
  }

  get isValidation(): boolean {
    return this.status === 400 && Object.keys(this.fieldErrors).length > 0
  }
}

const NETWORK_MESSAGE = 'We could not reach the server. Check your connection and try again.'
const GENERIC_MESSAGE = 'Something went wrong. Please try again.'

export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) {
    return error
  }

  if (axios.isAxiosError(error)) {
    if (!error.response) {
      return new ApiError({ status: 0, message: NETWORK_MESSAGE, code: 'network_error' })
    }

    const problem = (typeof error.response.data === 'object' ? error.response.data : undefined) as
      | ProblemDetails
      | undefined
    const fieldErrors = problem?.errors ?? {}
    const firstFieldMessage = Object.values(fieldErrors)[0]?.[0]

    return new ApiError({
      status: error.response.status,
      message: problem?.detail ?? firstFieldMessage ?? problem?.title ?? GENERIC_MESSAGE,
      code: problem?.code,
      title: problem?.title,
      fieldErrors,
      traceId: problem?.traceId,
    })
  }

  return new ApiError({ status: -1, message: error instanceof Error ? error.message : GENERIC_MESSAGE })
}

export function errorMessage(error: unknown, fallback = GENERIC_MESSAGE): string {
  const apiError = toApiError(error)
  return apiError.message || fallback
}

/**
 * Copies server-side field errors ("email": ["already taken"]) onto a react-hook-form instance.
 * Returns the messages that had no matching field so the caller can show them in a banner.
 */
export function applyFieldErrors<TField extends string>(
  error: unknown,
  knownFields: readonly TField[],
  setError: (field: TField, error: { type: string; message: string }) => void,
): string[] {
  const apiError = toApiError(error)
  const unmatched: string[] = []

  for (const [field, messages] of Object.entries(apiError.fieldErrors)) {
    const known = knownFields.find((f) => f === field)
    if (known) {
      setError(known, { type: 'server', message: messages[0] ?? 'Invalid value' })
    } else {
      unmatched.push(...messages)
    }
  }

  return unmatched
}
