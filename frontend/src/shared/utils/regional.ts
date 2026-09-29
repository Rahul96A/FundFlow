import type { SelectOption } from '@/components/forms/FormFields'

/** ISO 4217 codes offered in the sign-up and settings forms (the API accepts any valid three-letter code). */
const CURRENCY_CODES = [
  'USD', 'EUR', 'GBP', 'CAD', 'AUD', 'NZD', 'CHF', 'SEK', 'NOK', 'DKK', 'JPY', 'INR', 'SGD', 'HKD', 'ZAR', 'BRL', 'MXN', 'AED',
] as const

function currencyName(code: string): string {
  try {
    return new Intl.DisplayNames(['en'], { type: 'currency' }).of(code) ?? code
  } catch {
    return code
  }
}

export const COMMON_CURRENCIES: SelectOption[] = CURRENCY_CODES.map((code) => ({
  value: code,
  label: `${code} · ${currencyName(code)}`,
}))

export const COMMON_LOCALES: SelectOption[] = [
  { value: 'en-US', label: 'English (United States)' },
  { value: 'en-GB', label: 'English (United Kingdom)' },
  { value: 'en-CA', label: 'English (Canada)' },
  { value: 'en-AU', label: 'English (Australia)' },
  { value: 'fr-FR', label: 'Français (France)' },
  { value: 'fr-CA', label: 'Français (Canada)' },
  { value: 'de-DE', label: 'Deutsch (Deutschland)' },
  { value: 'es-ES', label: 'Español (España)' },
  { value: 'es-MX', label: 'Español (México)' },
  { value: 'pt-BR', label: 'Português (Brasil)' },
  { value: 'nl-NL', label: 'Nederlands (Nederland)' },
  { value: 'ja-JP', label: '日本語 (日本)' },
]

export const MONTHS: SelectOption[] = Array.from({ length: 12 }, (_, i) => ({
  value: String(i + 1),
  label: new Intl.DateTimeFormat('en-US', { month: 'long' }).format(new Date(2000, i, 1)),
}))

/** IANA time zones the browser knows, with UTC guaranteed to be present. */
export function timeZones(): string[] {
  try {
    const zones = Intl.supportedValuesOf('timeZone')
    return zones.includes('UTC') ? zones : ['UTC', ...zones]
  } catch {
    return ['UTC']
  }
}
