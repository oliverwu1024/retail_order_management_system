import { z } from 'zod'
import type { components } from '@/lib/api/schema'

// ─────────────────────────────────────────────────────────────────────────────
//  "My Account" form schemas (zod) + form→API mappers.
//
//  The zod rules mirror the backend FluentValidation rules (UpsertProfileRequest-
//  /AddressRequestValidator) so a bad value fails inline instead of round-tripping
//  to a 422. The mappers convert the form's "empty string" into the API's null and
//  upper-case the country to its canonical ISO-3166 alpha-2 form (the server does
//  the same, but doing it here keeps the optimistic UI consistent).
// ─────────────────────────────────────────────────────────────────────────────

const optionalText = (max: number) => z.string().trim().max(max)

export const profileFormSchema = z.object({
  displayName: z.string().trim().min(1, 'Display name is required').max(120),
  phone: optionalText(32).refine(
    (value) => value === '' || /^\+?[0-9\s\-()]{7,}$/.test(value),
    'Enter a valid phone number',
  ),
})

export type ProfileFormValues = z.infer<typeof profileFormSchema>

export const addressFormSchema = z.object({
  line1: z.string().trim().min(1, 'Address line 1 is required').max(200),
  line2: optionalText(200),
  city: z.string().trim().min(1, 'City is required').max(120),
  region: optionalText(120),
  postalCode: z.string().trim().min(1, 'Postal code is required').max(20),
  country: z
    .string()
    .trim()
    .regex(/^[A-Za-z]{2}$/, 'Use a 2-letter country code (e.g. AU, US)'),
  isDefaultShipping: z.boolean(),
  isDefaultBilling: z.boolean(),
})

export type AddressFormValues = z.infer<typeof addressFormSchema>

type Schemas = components['schemas']

// Trimmed text → the value, or null when blank (the API treats null as "unset").
function nullIfBlank(value: string): string | null {
  const trimmed = value.trim()
  return trimmed.length > 0 ? trimmed : null
}

export function toUpsertProfileBody(values: ProfileFormValues): Schemas['UpsertProfileRequest'] {
  return {
    displayName: values.displayName.trim(),
    phone: nullIfBlank(values.phone),
  }
}

export function toAddressBody(values: AddressFormValues): Schemas['AddressRequest'] {
  return {
    line1: values.line1.trim(),
    line2: nullIfBlank(values.line2),
    city: values.city.trim(),
    region: nullIfBlank(values.region),
    postalCode: values.postalCode.trim(),
    country: values.country.trim().toUpperCase(),
    isDefaultShipping: values.isDefaultShipping,
    isDefaultBilling: values.isDefaultBilling,
  }
}
