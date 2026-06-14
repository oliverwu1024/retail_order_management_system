import { useEffect, type ReactNode } from 'react'
import { apiClient } from '@/lib/api/client'
import { applyAuthUser } from './session'

/**
 * Runs once on app load: seeds the CSRF cookie (GET /auth/csrf) so later
 * state-changing requests carry the double-submit token, then resolves the current
 * session (GET /auth/me) into the auth store. A 401 (not signed in) resolves to a
 * null user — that flips the store's `isLoading` to false so guards stop blocking.
 */
export function SessionBootstrapper({ children }: { children: ReactNode }) {
  useEffect(() => {
    let cancelled = false

    async function bootstrap() {
      await apiClient.GET('/api/v1/auth/csrf')
      const { data } = await apiClient.GET('/api/v1/auth/me')
      if (!cancelled) {
        applyAuthUser(data?.data)
      }
    }

    void bootstrap().catch(() => {
      if (!cancelled) {
        applyAuthUser(null)
      }
    })

    return () => {
      cancelled = true
    }
  }, [])

  return <>{children}</>
}
