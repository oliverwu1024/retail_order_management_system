import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { ForecastPage, ReorderHintPage } from '@/lib/api/types'

export interface ForecastParams {
  page: number
  pageSize: number
}

export const forecastKeys = {
  all: ['admin', 'forecast'] as const,
  forecasts: (p: ForecastParams) => ['admin', 'forecast', 'list', p] as const,
  reorderHints: (p: ForecastParams) => ['admin', 'forecast', 'reorder', p] as const,
}

/** Latest demand forecast per variant (Forecast.View). */
export function useForecastQuery(params: ForecastParams) {
  return useQuery({
    queryKey: forecastKeys.forecasts(params),
    queryFn: async (): Promise<ForecastPage> => {
      const { data, error } = await apiClient.GET('/api/v1/analytics/forecast', {
        params: { query: { Page: params.page, PageSize: params.pageSize } },
      })
      if (error || !data?.data) {
        throw new Error('Failed to load forecasts.')
      }
      return data.data
    },
  })
}

/** Active reorder hints, ranked by recommended quantity (Forecast.View). */
export function useReorderHintsQuery(params: ForecastParams) {
  return useQuery({
    queryKey: forecastKeys.reorderHints(params),
    queryFn: async (): Promise<ReorderHintPage> => {
      const { data, error } = await apiClient.GET('/api/v1/analytics/reorder-hints', {
        params: { query: { Page: params.page, PageSize: params.pageSize } },
      })
      if (error || !data?.data) {
        throw new Error('Failed to load reorder hints.')
      }
      return data.data
    },
  })
}

/** Dismisses a reorder hint (clears it from the list). Invalidates the forecast queries on success. */
export function useDismissReorderHint() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: string) => {
      const { error } = await apiClient.POST('/api/v1/analytics/reorder-hints/{id}/dismiss', {
        params: { path: { id } },
      })
      if (error) {
        throw new Error('Failed to dismiss the reorder hint.')
      }
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: forecastKeys.all })
    },
  })
}
