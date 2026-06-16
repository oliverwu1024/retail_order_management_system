import { useMutation, useQueryClient } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { OrderDetail } from '@/lib/api/types'
import { orderKeys } from './useOrdersQuery'

/** Cancels a paid order (server refunds + restocks); returns the updated order. */
export function useCancelOrder() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: string): Promise<OrderDetail> => {
      const { data, error } = await apiClient.POST('/api/v1/orders/{id}/cancel', {
        params: { path: { id } },
      })
      if (error || !data?.data) {
        throw new Error('Failed to cancel the order.')
      }
      return data.data
    },
    onSuccess: (order) => {
      if (order.id) {
        queryClient.setQueryData(orderKeys.detail(order.id), order)
      }
      void queryClient.invalidateQueries({ queryKey: orderKeys.all })
    },
  })
}
