import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router-dom'
import { useAuthStore } from '@/lib/store/auth-store'

// ─────────────────────────────────────────────────────────────────────────────
//  <RoleGuard /> — route guard.
//
//  WHY GUARD ON THE FRONTEND IF THE BACKEND ALSO CHECKS?
//  ----------------------------------------------------
//  Backend authorization is the source of truth — never trust the client
//  to enforce access. This guard is purely UX: a non-admin who navigates
//  to /admin should see a friendly redirect, not a flash of admin UI
//  followed by a 403 toast.
//
//  HOW THIS COMPOSES WITH THE LOADING STATE:
//  ----------------------------------------
//  On first load, the auth store is in isLoading=true while the app
//  calls /api/auth/me to find out who's logged in. Rendering during that
//  window would either flash the public state or briefly block the admin
//  UI. We return null until isLoading flips false — visual no-op, no
//  flash, no race.
// ─────────────────────────────────────────────────────────────────────────────

interface RoleGuardProps {
  /** Roles allowed through. ANY match grants access. Empty means "authenticated user only." */
  allowedRoles?: string[]
  /** Where to send users who don't meet the role requirement. */
  redirectTo?: string
  children: ReactNode
}

export function RoleGuard({ allowedRoles = [], redirectTo = '/', children }: RoleGuardProps) {
  const { user, isLoading } = useAuthStore()
  const location = useLocation()

  if (isLoading) {
    // Auth hydration still in flight. Render nothing — better than a
    // flash of the wrong view.
    return null
  }

  if (!user) {
    // Not authenticated. Send to redirectTo, preserving the attempted
    // location so a future /login can route them back here on success.
    return <Navigate to={redirectTo} state={{ from: location }} replace />
  }

  if (allowedRoles.length > 0) {
    const hasRole = user.roles.some((role) => allowedRoles.includes(role))
    if (!hasRole) {
      return <Navigate to={redirectTo} replace />
    }
  }

  return <>{children}</>
}
