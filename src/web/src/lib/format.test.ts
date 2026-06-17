import { describe, expect, it } from 'vitest'
import { centsToDollars, dollarsToCents, formatCents } from './format'

describe('formatCents', () => {
  it('formats integer cents as USD', () => {
    expect(formatCents(1999)).toBe('$19.99')
    expect(formatCents(0)).toBe('$0.00')
    expect(formatCents(100000)).toBe('$1,000.00')
  })
})

describe('centsToDollars', () => {
  it('renders an editable two-decimal string', () => {
    expect(centsToDollars(1999)).toBe('19.99')
    expect(centsToDollars(500)).toBe('5.00')
  })

  it('returns an empty string for null/undefined', () => {
    expect(centsToDollars(null)).toBe('')
    expect(centsToDollars(undefined)).toBe('')
  })
})

describe('dollarsToCents', () => {
  it('parses a dollar string to integer cents', () => {
    expect(dollarsToCents('19.99')).toBe(1999)
    expect(dollarsToCents(5)).toBe(500)
  })

  it('rounds away binary float drift (0.1 * 100 !== 10 in IEEE-754)', () => {
    expect(dollarsToCents('0.10')).toBe(10)
    expect(dollarsToCents('29.30')).toBe(2930)
  })
})
