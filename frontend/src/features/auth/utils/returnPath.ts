import { paths } from '@/shared/constants/paths'

/**
 * Only same-site, absolute-path destinations are honoured after sign-in. Anything else (a full URL, a protocol-relative
 * "//evil.example", a backslash trick) falls back to the default: otherwise a crafted login link is an open redirect.
 */
export function safeReturnPath(candidate: unknown, fallback: string = paths.root): string {
  if (typeof candidate !== 'string') {
    return fallback
  }
  if (!candidate.startsWith('/') || candidate.startsWith('//') || candidate.includes('\\')) {
    return fallback
  }
  if (candidate === paths.login || candidate.startsWith(`${paths.login}?`)) {
    return fallback
  }
  return candidate
}
