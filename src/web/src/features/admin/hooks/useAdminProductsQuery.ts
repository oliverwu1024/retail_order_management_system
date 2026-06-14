import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { ProductPage } from '@/lib/api/types'

export interface AdminProductListParams {
  page: number
  pageSize: number
  categoryId?: string
  search?: string
}

/** Query keys for the admin product cache — mutations invalidate ['admin', 'products'] to refetch. */
export const adminProductKeys = {
  all: ['admin', 'products'] as const,
  list: (params: AdminProductListParams) => ['admin', 'products', 'list', params] as const,
  detail: (id: string) => ['admin', 'products', 'detail', id] as const,
}

/**
 * Paged product list for the admin table. Unlike the storefront's
 * useProductsQuery, this hits /catalog/admin/products, which includes
 * UNPUBLISHED products so admins can manage drafts.
 */
export function useAdminProductsQuery(params: AdminProductListParams) {
  return useQuery({
    queryKey: adminProductKeys.list(params),
    queryFn: async (): Promise<ProductPage> => {
      const { data, error } = await apiClient.GET('/api/v1/catalog/admin/products', {
        params: {
          // PascalCase: ASP.NET binds query params by property name, not the
          // camelCase JSON policy used for bodies/responses.
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
