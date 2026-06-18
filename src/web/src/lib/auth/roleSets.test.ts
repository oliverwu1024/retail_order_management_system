import { describe, expect, it } from 'vitest'
import { ADMIN_AREA_ROLES, ROLE_SETS, hasAnyRole } from './roleSets'

describe('hasAnyRole', () => {
  it('returns true when the user holds one of the allowed roles', () => {
    expect(hasAnyRole(['Staff'], ROLE_SETS.orders)).toBe(true)
  })

  it('returns false when the user holds none of the allowed roles', () => {
    expect(hasAnyRole(['Staff'], ROLE_SETS.catalog)).toBe(false)
  })

  it('returns false for undefined roles', () => {
    expect(hasAnyRole(undefined, ROLE_SETS.orders)).toBe(false)
  })

  it('returns false for an empty allowed list', () => {
    expect(hasAnyRole(['Administrator'], [])).toBe(false)
  })
})

// These lock the frontend capability map to the backend policy matrix
// (PHASE_3_SCOPE.md §3.1) — a drift here is a UX bug (a button shown that the
// server then rejects), so the matrix is asserted explicitly.
describe('ROLE_SETS capability matrix', () => {
  it('limits catalog management to Administrator only', () => {
    expect(hasAnyRole(['Staff'], ROLE_SETS.catalog)).toBe(false)
    expect(hasAnyRole(['StoreManager'], ROLE_SETS.catalog)).toBe(false)
    expect(hasAnyRole(['Administrator'], ROLE_SETS.catalog)).toBe(true)
  })

  it('limits refunds and user management to StoreManager and Administrator', () => {
    for (const set of [ROLE_SETS.refund, ROLE_SETS.users]) {
      expect(hasAnyRole(['Staff'], set)).toBe(false)
      expect(hasAnyRole(['StoreManager'], set)).toBe(true)
      expect(hasAnyRole(['Administrator'], set)).toBe(true)
    }
  })

  it('limits review sentiment to StoreManager and Administrator (Staff excluded, mirrors Sentiment.View)', () => {
    expect(hasAnyRole(['Staff'], ROLE_SETS.sentiment)).toBe(false)
    expect(hasAnyRole(['StoreManager'], ROLE_SETS.sentiment)).toBe(true)
    expect(hasAnyRole(['Administrator'], ROLE_SETS.sentiment)).toBe(true)
  })

  it('admits all three back-office roles to the view-level areas', () => {
    for (const set of [ROLE_SETS.orders, ROLE_SETS.inventory, ROLE_SETS.audit, ROLE_SETS.reports]) {
      expect(set).toEqual(ADMIN_AREA_ROLES)
    }
  })
})
