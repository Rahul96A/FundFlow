import { AxiosError, type InternalAxiosRequestConfig } from 'axios'
import { ApiError, applyFieldErrors, errorMessage, toApiError } from './problem'

function axiosError(status: number, data: unknown): AxiosError {
  const config = { headers: {} } as InternalAxiosRequestConfig
  return new AxiosError('boom', String(status), config, null, {
    status,
    statusText: '',
    headers: {},
    config,
    data,
  })
}

describe('toApiError', () => {
  it('reads an RFC 7807 body', () => {
    const error = toApiError(
      axiosError(409, { title: 'Conflict', detail: 'Already exists.', code: 'email_taken', traceId: 'abc', errors: { email: ['Taken.'] } }),
    )

    expect(error).toBeInstanceOf(ApiError)
    expect(error.status).toBe(409)
    expect(error.message).toBe('Already exists.')
    expect(error.code).toBe('email_taken')
    expect(error.traceId).toBe('abc')
    expect(error.fieldErrors).toEqual({ email: ['Taken.'] })
  })

  it('uses the first field message when there is no detail', () => {
    const error = toApiError(axiosError(400, { title: 'Validation failed', errors: { password: ['Too short.', 'Needs a digit.'] } }))

    expect(error.message).toBe('Too short.')
    expect(error.isValidation).toBe(true)
  })

  it('describes a network failure without pretending the server said something', () => {
    const error = toApiError(new AxiosError('Network Error', 'ERR_NETWORK'))

    expect(error.isNetworkError).toBe(true)
    expect(error.message).toContain('could not reach the server')
  })

  it('falls back to a generic message for empty or non-JSON bodies', () => {
    expect(toApiError(axiosError(500, '<html>oops</html>')).message).toBe('Something went wrong. Please try again.')
    expect(toApiError(axiosError(500, undefined)).message).toBe('Something went wrong. Please try again.')
  })

  it('handles arbitrary thrown values', () => {
    expect(toApiError(new Error('kaput')).message).toBe('kaput')
    expect(toApiError('a string').message).toBe('Something went wrong. Please try again.')
    expect(errorMessage(null)).toBe('Something went wrong. Please try again.')
  })
})

describe('applyFieldErrors', () => {
  it('copies known fields onto the form and returns the rest for a banner', () => {
    const calls: [string, string][] = []
    const unmatched = applyFieldErrors(
      axiosError(400, { errors: { email: ['Taken.'], organizationSlug: ['Reserved.'], other: ['General problem.'] } }),
      ['email', 'password'] as const,
      (field, error) => calls.push([field, error.message]),
    )

    expect(calls).toEqual([['email', 'Taken.']])
    expect(unmatched).toEqual(['Reserved.', 'General problem.'])
  })
})
