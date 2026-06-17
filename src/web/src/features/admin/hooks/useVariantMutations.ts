import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { components } from '@/lib/api/schema'
import type { ProductVariant } from '@/lib/api/types'
import { adminProductKeys } from './useAdminProductsQuery'

type Schemas = components['schemas']

// Variant writes change the product detail (its variant list + stock) and the
// storefront's "From $X" price, so both the admin and storefront caches are
// invalidated. Variants are keyed by their parent product id.
function invalidateProductCaches(queryClient: QueryClient, productId: string) {
  void queryClient.invalidateQueries({ queryKey: adminProductKeys.detail(productId) })
  void queryClient.invalidateQueries({ queryKey: adminProductKeys.all })
  void queryClient.invalidateQueries({ queryKey: ['products'] })
}

/** Adds a variant (and its initial stock) to a product. */
export function useAddVariant() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (vars: {
      productId: string
      body: Schemas['CreateVariantRequest']
    }): Promise<ProductVariant> => {
      const { data, error } = await apiClient.POST('/api/v1/catalog/products/{id}/variants', {
        params: { path: { id: vars.productId } },
        body: vars.body,
      })
      if (error || !data?.data) {
        throw new Error('Failed to add the variant.')
      }
      return data.data
    },
    onSuccess: (_data, vars) => invalidateProductCaches(queryClient, vars.productId),
  })
}

/**
 * Deactivates a variant (IsActive=false). The DELETE endpoint no longer hard-deletes:
 * orders and carts reference variants (RESTRICT FKs), so the row is preserved and just
 * hidden from the storefront and add-to-cart. Idempotent server-side.
 */
export function useDeactivateVariant() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (vars: { productId: string; variantId: string }): Promise<void> => {
      const { error } = await apiClient.DELETE(
        '/api/v1/catalog/products/{id}/variants/{variantId}',
        { params: { path: { id: vars.productId, variantId: vars.variantId } } },
      )
      if (error) {
        throw new Error('Failed to deactivate the variant.')
      }
    },
    onSuccess: (_data, vars) => invalidateProductCaches(queryClient, vars.productId),
  })
}

/**
 * Reactivates a previously deactivated variant. There is no "un-delete" verb, so this
 * PUTs the variant's current attributes back with IsActive=true — the variant update
 * endpoint doubles as the reactivate path.
 */
export function useReactivateVariant() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (vars: { productId: string; variant: ProductVariant }): Promise<void> => {
      const v = vars.variant
      const { error } = await apiClient.PUT('/api/v1/catalog/products/{id}/variants/{variantId}', {
        params: { path: { id: vars.productId, variantId: v.id ?? '' } },
        // Re-send the variant's current attributes; only IsActive changes.
        body: {
          options: v.options,
          priceCents: v.priceCents ?? 0,
          compareAtPriceCents: v.compareAtPriceCents ?? null,
          isActive: true,
        },
      })
      if (error) {
        throw new Error('Failed to reactivate the variant.')
      }
    },
    onSuccess: (_data, vars) => invalidateProductCaches(queryClient, vars.productId),
  })
}
