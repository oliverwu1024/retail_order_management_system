import { useQueryClient } from '@tanstack/react-query'
import type { components } from '@/lib/api/schema'
import { cartKey } from '@/features/cart/hooks/useCartQuery'
import { orderKeys } from '@/features/orders/hooks/useOrdersQuery'
import { applyAuthUser } from './session'

type AuthUserDto = components['schemas']['AuthUserDto']

/**
 * Auth-transition actions that keep the TanStack Query cache consistent with the
 * signed-in identity. Use these at the login / register / logout call sites instead of
 * calling applyAuthUser() directly, so the server-state cache can't bleed across the
 * auth boundary.
 *
 * WHY: the cart is one shared ['cart'] cache for guests and members, and on login the
 * server MERGES the guest cart into the member cart — so the pre-login cached count is
 * stale and must be refetched. Orders are member-scoped, so on logout we drop them (and
 * the cart) outright rather than show the previous member's data to the next viewer on a
 * shared device.
 *
 * (The QueryClient is deliberately useState-scoped per <AppProviders/>, so this reset has
 * to run through the useQueryClient() hook rather than a module-level client.)
 */
export function useSessionActions() {
  const queryClient = useQueryClient()

  return {
    /** Apply a successful login/register (cookies already set) + refetch the merged cart. */
    signIn(dto: AuthUserDto | null | undefined) {
      applyAuthUser(dto)
      void queryClient.invalidateQueries({ queryKey: cartKey })
    },
    /** Apply a logout + drop the prior member's cart/orders from the now-anonymous session. */
    signOut() {
      applyAuthUser(null)
      queryClient.removeQueries({ queryKey: cartKey })
      queryClient.removeQueries({ queryKey: orderKeys.all })
    },
  }
}
