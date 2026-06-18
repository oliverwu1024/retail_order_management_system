import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { SentimentSummary } from '@/lib/api/types'

/** Review-sentiment summary for the admin dashboard (Sentiment.View — StoreManager + Administrator). */
export function useSentimentSummaryQuery() {
  return useQuery({
    queryKey: ['admin', 'sentiment', 'summary'] as const,
    queryFn: async (): Promise<SentimentSummary> => {
      const { data, error } = await apiClient.GET('/api/v1/analytics/sentiment-summary')
      if (error || !data?.data) {
        throw new Error('Failed to load sentiment.')
      }
      return data.data
    },
  })
}
