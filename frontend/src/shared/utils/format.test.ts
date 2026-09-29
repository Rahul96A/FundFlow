import { describeUserAgent, formatDate, formatDateTime, formatMoney, humanizeAction, humanizeRole, initials } from './format'

describe('formatting', () => {
  it('shows UTC timestamps in the organization time zone', () => {
    // 03:30 UTC on 1 July is 23:30 the previous evening in New York (EDT, UTC-4) and 12:30 in Tokyo.
    expect(formatDateTime('2026-07-01T03:30:00Z', 'America/New_York')).toContain('Jun 30, 2026')
    expect(formatDateTime('2026-07-01T03:30:00Z', 'America/New_York')).toContain('11:30')
    expect(formatDateTime('2026-07-01T03:30:00Z', 'Asia/Tokyo')).toContain('Jul 1, 2026')
    expect(formatDate('2026-07-01T03:30:00Z', 'America/New_York')).toBe('Jun 30, 2026')
  })

  it('falls back gracefully for missing, invalid or unknown-zone input', () => {
    expect(formatDateTime(null)).toBe('—')
    expect(formatDateTime('not-a-date')).toBe('—')
    expect(formatDateTime('2026-07-01T03:30:00Z', 'Mars/Olympus')).not.toBe('—')
  })

  it('formats money with the currency and locale', () => {
    expect(formatMoney(1234.5, 'USD')).toBe('$1,234.50')
    expect(formatMoney(1234.5, 'EUR', 'de-DE')).toContain('1.234,50')
  })

  it.each([
    ['Ada Lovelace', 'AL'],
    ['  grace   brewster  hopper ', 'GH'],
    ['Plato', 'P'],
    ['', '?'],
  ])('derives initials from %j', (name, expected) => {
    expect(initials(name)).toBe(expected)
  })

  it('turns codes into readable words', () => {
    expect(humanizeAction('Auth.LoginFailed')).toBe('Login failed')
    expect(humanizeAction('User.RolesChanged')).toBe('Roles changed')
    expect(humanizeRole('FUNDRAISING_MANAGER')).toBe('Fundraising manager')
  })

  it('summarises a user agent without exposing the raw string', () => {
    expect(describeUserAgent('Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/126.0 Safari/537.36')).toBe('Chrome on Windows')
    expect(describeUserAgent('Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit Safari/604.1')).toBe('Safari on iOS')
    expect(describeUserAgent(null)).toBe('Unknown device')
  })
})
