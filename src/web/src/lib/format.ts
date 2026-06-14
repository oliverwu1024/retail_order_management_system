const currencyFormatter = new Intl.NumberFormat('en-US', {
  style: 'currency',
  currency: 'USD',
})

/** Formats integer cents as currency — e.g. 1999 → "$19.99". Money is cents end-to-end. */
export function formatCents(cents: number): string {
  return currencyFormatter.format(cents / 100)
}

// ─────────────────────────────────────────────────────────────────────────────
//  Dollar ↔ cents conversion for forms.
//
//  Money is integer cents end-to-end (DB, API, store) — never a float, so we
//  never accumulate the $0.01 rounding drift that floating-point dollars cause.
//  Forms are the one place a human types dollars; these helpers convert at that
//  boundary and nowhere else.
// ─────────────────────────────────────────────────────────────────────────────

/** Cents → editable dollar string for a form field — e.g. 1999 → "19.99". */
export function centsToDollars(cents: number | null | undefined): string {
  if (cents == null) {
    return ''
  }
  return (cents / 100).toFixed(2)
}

/** Dollar string from a form field → integer cents — e.g. "19.99" → 1999. Math.round kills float drift. */
export function dollarsToCents(dollars: string | number): number {
  const value = typeof dollars === 'number' ? dollars : Number.parseFloat(dollars)
  return Math.round(value * 100)
}
