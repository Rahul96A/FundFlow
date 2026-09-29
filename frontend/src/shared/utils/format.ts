import { formatDistanceToNowStrict, isValid, parseISO } from 'date-fns'

/** Falls back to the browser's zone when the organization zone is unknown or invalid. */
function safeZone(timeZone: string | null | undefined): string | undefined {
  if (!timeZone) {
    return undefined
  }
  try {
    const resolved = new Intl.DateTimeFormat('en-US', { timeZone }).resolvedOptions().timeZone
    return resolved ? timeZone : undefined
  } catch {
    return undefined
  }
}

/**
 * Timestamps are stored and transmitted in UTC; they are converted for display here, in the organization's
 * timezone, never earlier.
 */
export function formatDateTime(iso: string | null | undefined, timeZone?: string | null): string {
  if (!iso) {
    return '—'
  }
  const date = parseISO(iso)
  if (!isValid(date)) {
    return '—'
  }
  return new Intl.DateTimeFormat('en-US', {
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZone: safeZone(timeZone),
  }).format(date)
}

export function formatDate(iso: string | null | undefined, timeZone?: string | null): string {
  if (!iso) {
    return '—'
  }
  const date = parseISO(iso)
  if (!isValid(date)) {
    return '—'
  }
  return new Intl.DateTimeFormat('en-US', { dateStyle: 'medium', timeZone: safeZone(timeZone) }).format(date)
}

export function formatRelative(iso: string | null | undefined): string {
  if (!iso) {
    return 'Never'
  }
  const date = parseISO(iso)
  if (!isValid(date)) {
    return '—'
  }
  return `${formatDistanceToNowStrict(date)} ago`
}

export function formatNumber(value: number): string {
  return new Intl.NumberFormat('en-US').format(value)
}

export function formatMoney(amount: number, currency: string, locale = 'en-US'): string {
  return new Intl.NumberFormat(locale, { style: 'currency', currency }).format(amount)
}

export function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean)
  const first = parts[0]?.[0] ?? ''
  const last = parts.length > 1 ? (parts[parts.length - 1]?.[0] ?? '') : ''
  return (first + last).toUpperCase() || '?'
}

/** "Auth.LoginFailed" → "Login failed". */
export function humanizeAction(action: string): string {
  const name = action.split('.').slice(1).join(' ') || action
  const spaced = name.replace(/([a-z0-9])([A-Z])/g, '$1 $2').toLowerCase()
  return spaced.charAt(0).toUpperCase() + spaced.slice(1)
}

/** "SYSTEM_ADMIN" → "System admin". */
export function humanizeRole(role: string): string {
  const spaced = role.replace(/_/g, ' ').toLowerCase()
  return spaced.charAt(0).toUpperCase() + spaced.slice(1)
}

/** A short, friendly description of a browser from its User-Agent string. */
export function describeUserAgent(userAgent: string | null | undefined): string {
  if (!userAgent) {
    return 'Unknown device'
  }
  const browser = /Edg\//.test(userAgent)
    ? 'Edge'
    : /Chrome\//.test(userAgent)
      ? 'Chrome'
      : /Firefox\//.test(userAgent)
        ? 'Firefox'
        : /Safari\//.test(userAgent)
          ? 'Safari'
          : 'Browser'
  const os = /Windows/.test(userAgent)
    ? 'Windows'
    : /Android/.test(userAgent)
      ? 'Android'
      : /iPhone|iPad|iOS/.test(userAgent)
        ? 'iOS'
        : /Mac OS X/.test(userAgent)
          ? 'macOS'
          : /Linux/.test(userAgent)
            ? 'Linux'
            : 'unknown OS'
  return `${browser} on ${os}`
}
