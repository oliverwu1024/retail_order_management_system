import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { CustomerProfile } from '@/lib/api/types'

/** Query key for the current customer's profile (mutations invalidate it to refetch). */
export const profileKey = ['profile'] as const

/**
 * The signed-in customer's profile (+ addresses). The backend lazily creates the
 * profile on first GET, so this never 404s for a real customer.
 */
export function useProfileQuery() {
  return useQuery({
    queryKey: profileKey,
    queryFn: async (): Promise<CustomerProfile> => {
      const { data, error } = await apiClient.GET('/api/v1/profile')
      if (error || !data?.data) {
        throw new Error('Failed to load your account.')
      }
      return data.data
    },
  })
}
