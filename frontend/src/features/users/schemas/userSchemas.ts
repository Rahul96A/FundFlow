import { z } from 'zod'

const person = (label: string) => z.string().trim().min(1, `Enter ${label}.`).max(100, 'That is too long.')

const phone = z
  .string()
  .trim()
  .max(30, 'That phone number is too long.')
  .refine((v) => v === '' || /^[+()\-.\s0-9xX]{5,30}$/.test(v), 'Enter a valid phone number.')

export const inviteUserSchema = z.object({
  email: z
    .string()
    .trim()
    .min(1, 'Enter an email address.')
    .max(254, 'That email address is too long.')
    .refine((v) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v), 'Enter a valid email address.'),
  firstName: person('a first name'),
  lastName: person('a last name'),
  phoneNumber: phone,
  roleIds: z.array(z.string()).min(1, 'Choose at least one role.'),
})
export type InviteUserForm = z.infer<typeof inviteUserSchema>

export const editUserSchema = z.object({
  firstName: person('a first name'),
  lastName: person('a last name'),
  phoneNumber: phone,
})
export type EditUserForm = z.infer<typeof editUserSchema>
