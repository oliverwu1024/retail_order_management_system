import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { Cart } from '@/lib/api/types'

/** TanStack Query key for the caller's cart — a single per-caller resource. */
export const cartKey = ['cart'] as const

/**
 * The caller's current cart. Works for guests (anon-cart cookie) and members alike; the
 * cart page and the header badge both call this, sharing one cache entry, so a mutation
 * that writes the cache refreshes both at once.
 */
export function useCartQuery() {
  return useQuery({
    queryKey: cartKey,
    queryFn: async (): Promise<Cart> => {
      const { data, error } = await apiClient.GET('/api/v1/cart')
      if (error || !data?.data) {
        throw new Error('Failed to load your cart.')
      }
      return data.data
    },
    // Carts change often (other tabs, checkout) — revalidate on mount instead of trusting
    // the global 30s staleTime. Mutations also push fresh cart state into this cache.
    staleTime: 0,
  })
}
