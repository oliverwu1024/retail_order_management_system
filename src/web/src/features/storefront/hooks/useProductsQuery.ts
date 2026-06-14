import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { ProductPage } from '@/lib/api/types'

export interface ProductListParams {
  page: number
  pageSize: number
  categoryId?: string
  search?: string
}

/** Paged catalogue listing (published products) with optional category filter + search. */
export function useProductsQuery(params: ProductListParams) {
  return useQuery({
    queryKey: ['products', params],
    queryFn: async (): Promise<ProductPage> => {
      const { data, error } = await apiClient.GET('/api/v1/catalog/products', {
        params: {
          // Query-string params are PascalCase (ASP.NET documents them by the bound
          // property name, NOT the camelCase JSON policy used for bodies/responses).
          query: {
            Page: params.page,
            PageSize: params.pageSize,
            CategoryId: params.categoryId,
            Search: params.search,
          },
        },
      })
      if (error || !data?.data) {
        throw new Error('Failed to load products.')
      }
      return data.data
    },
  })
}
