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
  /** Role claims, e.g. ['Customer'] or ['Administrator', 'StoreManager']. */
  roles: string[]
}

interface AuthState {
  user: AuthUser | null
  /** True while a /api/auth/me check is in flight on first load. */
  isLoading: boolean
  /**
   * Monotonic counter bumped on every EXPLICIT auth write (login / register / logout, which all
   * flow through setUser). It lets an async hydration discard itself if a newer sign-in/out won
   * the race in the meantime — see SessionBootstrapper. Without it, a slow /auth/me issued before
   * login can resolve AFTER login and clobber the freshly-authenticated user back to null.
   */
  authEpoch: number
  setUser: (user: AuthUser | null) => void
  setLoading: (loading: boolean) => void
}

export const useAuthStore = create<AuthState>((set) => ({
  user: null,
  isLoading: true,
  authEpoch: 0,
  setUser: (user) => set((state) => ({ user, isLoading: false, authEpoch: state.authEpoch + 1 })),
  setLoading: (isLoading) => set({ isLoading }),
}))

/** Convenience: read the current user from non-React code (interceptors, etc). */
export function getCurrentUser(): AuthUser | null {
  return useAuthStore.getState().user
}

/** Reads the current auth-write epoch (see {@link AuthState.authEpoch}). */
export function getAuthEpoch(): number {
  return useAuthStore.getState().authEpoch
}
