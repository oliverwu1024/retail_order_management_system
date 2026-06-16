import { useMutation, useQueryClient } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { Cart } from '@/lib/api/types'
import { cartKey } from './useCartQuery'

// ─────────────────────────────────────────────────────────────────────────────
//  Cart mutations (add / update qty / remove / clear).
//
//  Every cart endpoint returns the full, authoritative cart, so each mutation writes
//  that result straight into the cache with setQueryData instead of invalidating +
//  refetching. One round-trip updates both the cart page and the header badge instantly.
//  The CSRF header is attached automatically by the client middleware on these writes.
// ─────────────────────────────────────────────────────────────────────────────

/** Adds a variant to the cart (or bumps its quantity if already present). */
export function useAddToCart() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (vars: { productVariantId: string; quantity: number }): Promise<Cart> => {
      const { data, error } = await apiClient.POST('/api/v1/cart/items', { body: vars })
      if (error || !data?.data) {
        throw new Error('Failed to add to cart.')
      }
      return data.data
    },
    onSuccess: (cart) => queryClient.setQueryData(cartKey, cart),
  })
}

/** Sets the absolute quantity of a line. */
export function useUpdateCartItem() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (vars: { productVariantId: string; quantity: number }): Promise<Cart> => {
      const { data, error } = await apiClient.PUT('/api/v1/cart/items/{variantId}', {
        params: { path: { variantId: vars.productVariantId } },
        body: { quantity: vars.quantity },
      })
      if (error || !data?.data) {
        throw new Error('Failed to update the cart.')
      }
      return data.data
    },
    onSuccess: (cart) => queryClient.setQueryData(cartKey, cart),
  })
}

/** Removes a line from the cart. */
export function useRemoveCartItem() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (productVariantId: string): Promise<Cart> => {
      const { data, error } = await apiClient.DELETE('/api/v1/cart/items/{variantId}', {
        params: { path: { variantId: productVariantId } },
      })
      if (error || !data?.data) {
        throw new Error('Failed to remove the item.')
      }
      return data.data
    },
    onSuccess: (cart) => queryClient.setQueryData(cartKey, cart),
  })
}

/** Empties the cart. */
export function useClearCart() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (): Promise<Cart> => {
      const { data, error } = await apiClient.DELETE('/api/v1/cart')
      if (error || !data?.data) {
        throw new Error('Failed to clear the cart.')
      }
      return data.data
    },
    onSuccess: (cart) => queryClient.setQueryData(cartKey, cart),
  })
}
