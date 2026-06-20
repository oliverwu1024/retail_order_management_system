// Mirrors the backend capability matrix (Roles.Policies.* — PHASE_3_SCOPE.md §3.1). Frontend auth
// is UX-only — the server re-checks every request — but keeping the role→capability mapping in ONE
// place keeps the route guards, the sidebar, and per-element gating consistent and one-edit-to-change
// (no role strings scattered across components).

/** Any back-office role — admits a user to the /admin area at all. */
export const ADMIN_AREA_ROLES: string[] = ['Staff', 'StoreManager', 'Administrator']

/** Per-area role sets, mirroring the backend capability policies. */
export const ROLE_SETS = {
  orders: ['Staff', 'StoreManager', 'Administrator'],
  inventory: ['Staff', 'StoreManager', 'Administrator'],
  audit: ['Staff', 'StoreManager', 'Administrator'],
  reports: ['Staff', 'StoreManager', 'Administrator'],
  users: ['StoreManager', 'Administrator'],
  catalog: ['Administrator'],
  // Review-sentiment dashboard — StoreManager + Administrator (Staff excluded), mirrors Sentiment.View.
  sentiment: ['StoreManager', 'Administrator'],
  // Chat-session diagnostics — StoreManager + Administrator (Staff excluded), mirrors Chat.View.
  chat: ['StoreManager', 'Administrator'],
  // Capability (not a sidebar area): who may issue a refund — mirrors Orders.Refund.
  refund: ['StoreManager', 'Administrator'],
} satisfies Record<string, string[]>

export type AdminArea = keyof typeof ROLE_SETS

/** True if the user holds any of the allowed roles. UX gating only — the backend is the gate. */
export function hasAnyRole(
  userRoles: readonly string[] | undefined,
  allowed: readonly string[],
): boolean {
  return userRoles?.some((role) => allowed.includes(role)) ?? false
}
