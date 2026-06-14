import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { ProductDetail } from '@/lib/api/types'

/** A single published product by slug, with variants + stock. */
export function useProductQuery(slug: string | undefined) {
  return useQuery({
    queryKey: ['product', slug],
    enabled: Boolean(slug),
    queryFn: async (): Promise<ProductDetail> => {
      const { data, error } = await apiClient.GET('/api/v1/catalog/products/{slug}', {
        params: { path: { slug: slug as string } },
      })
      if (error || !data?.data) {
        throw new Error('Product not found.')
      }
      return data.data
    },
  })
}
