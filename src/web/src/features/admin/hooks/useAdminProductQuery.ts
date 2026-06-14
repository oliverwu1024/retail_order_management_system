import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { ProductDetail } from '@/lib/api/types'
import { adminProductKeys } from './useAdminProductsQuery'

/**
 * Loads one product by id for the edit form via /catalog/admin/products/{id}.
 * Uses the admin endpoint (not the public by-slug read) so it can load
 * unpublished drafts. `enabled` lets the create form skip the fetch.
 */
export function useAdminProductQuery(id: string | undefined) {
  return useQuery({
    queryKey: adminProductKeys.detail(id ?? ''),
    enabled: Boolean(id),
    queryFn: async (): Promise<ProductDetail> => {
      const { data, error } = await apiClient.GET('/api/v1/catalog/admin/products/{id}', {
        params: { path: { id: id! } },
      })
      if (error || !data?.data) {
        throw new Error('Failed to load product.')
      }
      return data.data
    },
  })
}
