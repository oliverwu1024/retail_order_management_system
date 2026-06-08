import { create } from 'zustand'

// ─────────────────────────────────────────────────────────────────────────────
//  useAuthStore — minimal Zustand store for the current user identity.
//
//  WHY Zustand AND NOT React Context FOR AUTH STATE?
//  -------------------------------------------------
//  Zustand stores live OUTSIDE React. That matters because the apiClient
//  fetch interceptor (lib/api/client.ts) needs to read the current user
//  WITHOUT being a React component. Context can't do that without hacks.
//  With Zustand: `useAuthStore.getState().user` works from anywhere.
//
//  WHY ZUSTAND AND NOT REDUX?
//  --------------------------
//  Tiny API (one file), no provider boilerplate, no reducers, TypeScript
//  inference works out of the box. Redux is overkill for a few stores of
//  flat data that don't need time-travel debugging.
//
//  Auth state itself does NOT hold the JWT — the access token is in an
//  HTTP-only cookie set by the backend and never reachable from JS (see
//  ADR-0007). This store holds only the safe-to-read user profile fields.
// ─────────────────────────────────────────────────────────────────────────────

export interface AuthUser {
  /** Identity user Id (string GUID, matches ApplicationUser.Id). */
  id: string
  /** Login email. */
  email: string
  /** Role claims, e.g. ['Customer'] or ['Admin', 'StoreManager']. */
  roles: string[]
}

interface AuthState {
  user: AuthUser | null
  /** True while a /api/auth/me check is in flight on first load. */
  isLoading: boolean
  setUser: (user: AuthUser | null) => void
  setLoading: (loading: boolean) => void
}

export const useAuthStore = create<AuthState>((set) => ({
  user: null,
  isLoading: true,
  setUser: (user) => set({ user, isLoading: false }),
  setLoading: (isLoading) => set({ isLoading }),
}))

/** Convenience: read the current user from non-React code (interceptors, etc). */
export function getCurrentUser(): AuthUser | null {
  return useAuthStore.getState().user
}
