import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { Category } from '@/lib/api/types'

/** All non-deleted categories (for the storefront filter dropdown). */
export function useCategoriesQuery() {
  return useQuery({
    queryKey: ['categories'],
    queryFn: async (): Promise<Category[]> => {
      const { data, error } = await apiClient.GET('/api/v1/catalog/categories')
      if (error || !data?.data) {
        throw new Error('Failed to load categories.')
      }
      return [...data.data]
    },
  })
}
