import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { AnomalyPage } from '@/lib/api/types'

export interface RiskQueueParams {
  page: number
  pageSize: number
}

export const riskQueueKeys = {
  all: ['admin', 'risk'] as const,
  list: (params: RiskQueueParams) => ['admin', 'risk', 'list', params] as const,
}

/** The order-anomaly Risk Queue: unacknowledged flagged orders, newest first (Anomaly.Manage). */
export function useRiskQueueQuery(params: RiskQueueParams) {
  return useQuery({
    queryKey: riskQueueKeys.list(params),
    queryFn: async (): Promise<AnomalyPage> => {
      const { data, error } = await apiClient.GET('/api/v1/analytics/anomalies', {
        params: { query: { Page: params.page, PageSize: params.pageSize } },
      })
      if (error || !data?.data) {
        throw new Error('Failed to load the risk queue.')
      }
      return data.data
    },
  })
}

/** Acknowledges a flagged order (clears its Mark-Shipped block). Invalidates the queue on success. */
export function useAcknowledgeAnomaly() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: string) => {
      const { error } = await apiClient.POST('/api/v1/analytics/anomalies/{id}/acknowledge', {
        params: { path: { id } },
      })
      if (error) {
        throw new Error('Failed to acknowledge the anomaly.')
      }
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: riskQueueKeys.all })
    },
  })
}
