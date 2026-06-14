import type { components } from '@/lib/api/schema'
import { useAuthStore } from '@/lib/store/auth-store'

type AuthUserDto = components['schemas']['AuthUserDto']

/**
 * Maps the API's AuthUserDto (whose fields are optional in the generated schema)
 * into the store's AuthUser, or clears the session when given null/undefined.
 * Uses getState() so it's callable from effects and event handlers alike.
 */
export function applyAuthUser(dto: AuthUserDto | null | undefined) {
  const { setUser } = useAuthStore.getState()
  if (!dto) {
    setUser(null)
    return
  }
  setUser({ id: dto.id ?? '', email: dto.email ?? '', roles: dto.roles ?? [] })
}
