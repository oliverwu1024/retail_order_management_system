import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { OrderDetail, OrderPage } from '@/lib/api/types'

/** Query keys for the customer's orders (list + detail). */
export const orderKeys = {
  all: ['orders'] as const,
  list: (page: number) => ['orders', 'list', page] as const,
  detail: (id: string) => ['orders', 'detail', id] as const,
}

/** The current customer's orders (paged, newest first). */
export function useOrdersQuery(page: number, pageSize = 20) {
  return useQuery({
    queryKey: orderKeys.list(page),
    queryFn: async (): Promise<OrderPage> => {
      const { data, error } = await apiClient.GET('/api/v1/orders', {
        // This endpoint's query params bind by its (lowercase) method parameter names.
        params: { query: { page, pageSize } },
      })
      if (error || !data?.data) {
        throw new Error('Failed to load your orders.')
      }
      return data.data
    },
  })
}

/** One of the customer's orders by id (404 if not theirs). */
export function useOrderQuery(id: string | undefined) {
  return useQuery({
    queryKey: orderKeys.detail(id ?? ''),
    enabled: Boolean(id),
    queryFn: async (): Promise<OrderDetail> => {
      const { data, error } = await apiClient.GET('/api/v1/orders/{id}', {
        params: { path: { id: id! } },
      })
      if (error || !data?.data) {
        throw new Error('Failed to load the order.')
      }
      return data.data
    },
  })
}
