import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { SalesReport } from '@/lib/api/types'

/** Sales-by-day report (Reports.View). Defaults to the last 30 days (server-side). */
export function useSalesReportQuery() {
  return useQuery({
    queryKey: ['admin', 'reports', 'sales-by-day'] as const,
    queryFn: async (): Promise<SalesReport> => {
      const { data, error } = await apiClient.GET('/api/v1/analytics/sales-by-day')
      if (error || !data?.data) {
        throw new Error('Failed to load the sales report.')
      }
      return data.data
    },
  })
}
