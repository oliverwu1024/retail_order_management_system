import { useEffect, type ReactNode } from 'react'
import { apiClient } from '@/lib/api/client'
import { getAuthEpoch, useAuthStore } from '@/lib/store/auth-store'
import { applyAuthUser } from './session'

/**
 * Runs once on app load: seeds the CSRF cookie (GET /auth/csrf) so later
 * state-changing requests carry the double-submit token, then resolves the current
 * session (GET /auth/me) into the auth store. A 401 (not signed in) leaves a null user.
 *
 * RACE GUARD: /auth/me is fired at mount, BEFORE the user can log in. On a slow/cold backend
 * it can still be in flight when a login completes — and a stale 401 resolving afterwards used
 * to overwrite the freshly-signed-in user back to null (the intermittent "logged in but bounced
 * to /login" bug). We snapshot the auth epoch before fetching and refuse to write if an explicit
 * sign-in/out advanced it meanwhile; and on an anonymous 401 we only flip isLoading off rather
 * than setUser(null) (which would itself bump the epoch and could clobber a concurrent login).
 */
export function SessionBootstrapper({ children }: { children: ReactNode }) {
  useEffect(() => {
    let cancelled = false
    const startEpoch = getAuthEpoch()

    async function bootstrap() {
      await apiClient.GET('/api/v1/auth/csrf')
      const { data } = await apiClient.GET('/api/v1/auth/me')
      // Unmounted, or a login/logout already won the race → drop this stale hydration.
      if (cancelled || getAuthEpoch() !== startEpoch) {
        return
      }
      if (data?.data) {
        applyAuthUser(data.data) // a real signed-in user
      } else {
        useAuthStore.getState().setLoading(false) // anonymous (401) — don't clobber to null
      }
    }

    void bootstrap().catch(() => {
      // Network failure (a 401 doesn't throw — openapi-fetch returns { error }). Settle the
      // loading state without nulling a user a concurrent login may have just set.
      if (!cancelled && getAuthEpoch() === startEpoch) {
        useAuthStore.getState().setLoading(false)
      }
    })

    return () => {
      cancelled = true
    }
  }, [])

  return <>{children}</>
}
