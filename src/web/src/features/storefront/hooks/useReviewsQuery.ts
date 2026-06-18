import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { ReviewList } from '@/lib/api/types'

/** Query keys for a product's reviews — `product(id)` is the invalidation prefix the mutation bumps. */
export const reviewKeys = {
  all: ['reviews'] as const,
  product: (productId: string) => ['reviews', productId] as const,
  list: (productId: string, page: number) => ['reviews', productId, page] as const,
}

/** A page of a product's reviews plus the whole-product aggregate (average + distribution). Public. */
export function useReviewsQuery(productId: string | undefined, page: number, pageSize = 10) {
  return useQuery({
    queryKey: reviewKeys.list(productId ?? '', page),
    enabled: Boolean(productId),
    queryFn: async (): Promise<ReviewList> => {
      const { data, error } = await apiClient.GET('/api/v1/products/{productId}/reviews', {
        // PascalCase to match the ReviewListQuery DTO (consistent with the rest of the API).
        params: {
          path: { productId: productId as string },
          query: { Page: page, PageSize: pageSize },
        },
      })
      if (error || !data?.data) {
        throw new Error('Failed to load reviews.')
      }
      return data.data
    },
  })
}
