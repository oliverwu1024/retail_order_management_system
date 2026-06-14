const currencyFormatter = new Intl.NumberFormat('en-US', {
  style: 'currency',
  currency: 'USD',
})

/** Formats integer cents as currency — e.g. 1999 → "$19.99". Money is cents end-to-end. */
export function formatCents(cents: number): string {
  return currencyFormatter.format(cents / 100)
}
