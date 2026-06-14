import { z } from 'zod'
import type { components } from '@/lib/api/schema'
import { dollarsToCents } from '@/lib/format'

// ─────────────────────────────────────────────────────────────────────────────
//  Admin form schemas (zod) + form→API mappers.
//
//  WHY zod SCHEMAS THAT MIRROR THE BACKEND FluentValidation RULES?
//  --------------------------------------------------------------
//  The server is the source of truth (its validators reject bad input with
//  422), but echoing the same length/required rules client-side gives instant
//  inline feedback instead of a round-trip per mistake. The pairs are kept in
//  sync deliberately: SKU ≤ 64, Name ≤ 200, Slug ≤ 160, SEO title ≤ 200, SEO
//  description ≤ 400, brand ≤ 120, money ≥ 0 — see Validators/*.cs.
//
//  WHY MAPPERS HERE INSTEAD OF IN THE COMPONENTS?
//  ---------------------------------------------
//  The form speaks in dollars and empty strings (what a human types); the API
//  speaks in integer cents and nulls. Converting at this single boundary keeps
//  the components declarative and makes the conversion logic unit-testable on
//  its own. Money is cents end-to-end — dollars exist only inside the form.
// ─────────────────────────────────────────────────────────────────────────────

/** Optional text field: trims, allows empty, caps length. Empty string ≡ "not provided". */
const optionalText = (max: number) => z.string().trim().max(max)

export const productFormSchema = z.object({
  // SKU is required to create and is immutable afterwards — the edit form shows it disabled.
  sku: z.string().trim().min(1, 'SKU is required').max(64),
  name: z.string().trim().min(1, 'Name is required').max(200),
  // Slug is optional; the backend slugifies the name when it's blank.
  slug: optionalText(160),
  brandName: optionalText(120),
  description: optionalText(4000),
  seoTitle: optionalText(200),
  seoDescription: optionalText(400),
  // Empty string fails min(1) → "A category is required" (a chosen option is always a GUID).
  categoryId: z.string().min(1, 'A category is required'),
  isPublished: z.boolean(),
})

export type ProductFormValues = z.infer<typeof productFormSchema>

/** A single key/value option row in the variant editor (e.g. Size → "M"). */
const optionRowSchema = z.object({
  key: z.string().trim().max(50),
  value: z.string().trim().max(100),
})

export const variantFormSchema = z.object({
  sku: z.string().trim().min(1, 'SKU is required').max(64),
  // z.coerce turns the <input> string into a number; the message guards against a blank field.
  priceDollars: z.coerce
    .number({ invalid_type_error: 'Enter a price' })
    .min(0, 'Price must be at least 0'),
  // Optional: an empty field means "no compare-at price", so preprocess '' → undefined.
  compareAtDollars: z.preprocess(
    (raw) => (raw === '' || raw == null ? undefined : raw),
    z.coerce.number({ invalid_type_error: 'Enter a number' }).min(0).optional(),
  ),
  initialStock: z.coerce
    .number({ invalid_type_error: 'Enter a quantity' })
    .int('Whole units only')
    .min(0, 'Stock must be at least 0'),
  options: z.array(optionRowSchema),
})

export type VariantFormValues = z.infer<typeof variantFormSchema>

type Schemas = components['schemas']

// Trimmed text → the value, or null when blank (the API treats null as "unset").
function nullIfBlank(value: string): string | null {
  const trimmed = value.trim()
  return trimmed.length > 0 ? trimmed : null
}

/** Maps validated product form values → the CreateProductRequest body. */
export function toCreateProductBody(values: ProductFormValues): Schemas['CreateProductRequest'] {
  return {
    sku: values.sku.trim(),
    name: values.name.trim(),
    slug: nullIfBlank(values.slug),
    description: nullIfBlank(values.description),
    seoTitle: nullIfBlank(values.seoTitle),
    seoDescription: nullIfBlank(values.seoDescription),
    brandName: nullIfBlank(values.brandName),
    categoryId: values.categoryId,
    isPublished: values.isPublished,
  }
}

/** Maps validated product form values → the UpdateProductRequest body (SKU is immutable, so omitted). */
export function toUpdateProductBody(values: ProductFormValues): Schemas['UpdateProductRequest'] {
  return {
    name: values.name.trim(),
    slug: nullIfBlank(values.slug),
    description: nullIfBlank(values.description),
    seoTitle: nullIfBlank(values.seoTitle),
    seoDescription: nullIfBlank(values.seoDescription),
    brandName: nullIfBlank(values.brandName),
    categoryId: values.categoryId,
    isPublished: values.isPublished,
  }
}

/** Builds the options map from the editor rows, keeping only rows where both key and value are filled. */
function toOptionsMap(rows: VariantFormValues['options']): Record<string, string> | null {
  const entries = rows
    .filter((row) => row.key.trim().length > 0 && row.value.trim().length > 0)
    .map((row) => [row.key.trim(), row.value.trim()] as const)
  return entries.length > 0 ? Object.fromEntries(entries) : null
}

/** Maps validated variant form values → the CreateVariantRequest body (dollars → integer cents). */
export function toCreateVariantBody(values: VariantFormValues): Schemas['CreateVariantRequest'] {
  return {
    sku: values.sku.trim(),
    priceCents: dollarsToCents(values.priceDollars),
    compareAtPriceCents:
      values.compareAtDollars == null ? null : dollarsToCents(values.compareAtDollars),
    initialStock: values.initialStock,
    options: toOptionsMap(values.options),
  }
}
