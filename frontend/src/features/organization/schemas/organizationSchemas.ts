import { z } from 'zod'

const optionalText = (max: number, label: string) => z.string().trim().max(max, `${label} is too long.`)

const httpUrl = (label: string) =>
  z
    .string()
    .trim()
    .max(500, `${label} is too long.`)
    .refine((value) => {
      if (value === '') {
        return true
      }
      try {
        const url = new URL(value)
        return url.protocol === 'http:' || url.protocol === 'https:'
      } catch {
        return false
      }
    }, 'Enter a full web address starting with http:// or https://.')

export const organizationProfileSchema = z.object({
  name: z.string().trim().min(1, 'Enter the organization name.').max(200, 'That name is too long.'),
  legalName: optionalText(200, 'The legal name'),
  taxId: optionalText(50, 'The tax ID'),
  website: httpUrl('The website address'),
  contactEmail: z
    .string()
    .trim()
    .min(1, 'Enter a contact email.')
    .refine((value) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value), 'Enter a valid email address.'),
  phoneNumber: z
    .string()
    .trim()
    .max(30, 'That phone number is too long.')
    .refine((value) => value === '' || /^[+()\-.\s0-9xX]{5,30}$/.test(value), 'Enter a valid phone number.'),
  line1: optionalText(200, 'The address'),
  line2: optionalText(200, 'The address'),
  city: optionalText(100, 'The city'),
  region: optionalText(100, 'The state or region'),
  postalCode: optionalText(20, 'The postal code'),
  country: optionalText(100, 'The country'),
})
export type OrganizationProfileForm = z.infer<typeof organizationProfileSchema>

export const organizationSettingsSchema = z.object({
  timeZoneId: z.string().min(1, 'Choose a time zone.'),
  currencyCode: z.string().length(3, 'Choose a currency.'),
  locale: z.string().min(1, 'Choose a locale.'),
  fiscalYearStartMonth: z.string().min(1, 'Choose a month.'),
  logoUrl: httpUrl('The logo address'),
  brandColor: z.string().trim().refine((value) => value === '' || /^#[0-9A-Fa-f]{6}$/.test(value), 'Use a hex colour like #1F5EFF.'),
})
export type OrganizationSettingsForm = z.infer<typeof organizationSettingsSchema>
