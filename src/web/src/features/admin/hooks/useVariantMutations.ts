import { useMutation, useQueryClient } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { components } from '@/lib/api/schema'
import type { ProductVariant } from '@/lib/api/types'
import { adminProductKeys } from './useAdminProductsQuery'

type Schemas = components['schemas']

// Variant writes change the product detail (its variant list + stock) and the
// storefront's "From $X" price, so both caches are invalidated. Variants are
// keyed by their parent product id.

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
    onSuccess: (_data, vars) => {
      void queryClient.invalidateQueries({ queryKey: adminProductKeys.detail(vars.productId) })
      void queryClient.invalidateQueries({ queryKey: adminProductKeys.all })
      void queryClient.invalidateQueries({ queryKey: ['products'] })
    },
  })
}

/** Removes a variant from a product. */
export function useDeleteVariant() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (vars: { productId: string; variantId: string }): Promise<void> => {
      const { error } = await apiClient.DELETE(
        '/api/v1/catalog/products/{id}/variants/{variantId}',
        { params: { path: { id: vars.productId, variantId: vars.variantId } } },
      )
      if (error) {
        throw new Error('Failed to delete the variant.')
      }
    },
    onSuccess: (_data, vars) => {
      void queryClient.invalidateQueries({ queryKey: adminProductKeys.detail(vars.productId) })
      void queryClient.invalidateQueries({ queryKey: adminProductKeys.all })
      void queryClient.invalidateQueries({ queryKey: ['products'] })
    },
  })
}
