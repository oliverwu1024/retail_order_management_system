import { useMutation, useQueryClient } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { components } from '@/lib/api/schema'
import type { CustomerProfile, Address } from '@/lib/api/types'
import { profileKey } from './useProfileQuery'

type Schemas = components['schemas']

// Every account mutation invalidates the single ['profile'] query, which carries the
// profile + its addresses — so one refetch keeps the whole page in sync. The CSRF
// header is attached automatically by the client middleware on these writes.

/** Updates DisplayName + Phone (Email is immutable server-side). */
export function useUpdateProfile() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: Schemas['UpsertProfileRequest']): Promise<CustomerProfile> => {
      const { data, error } = await apiClient.PUT('/api/v1/profile', { body })
      if (error || !data?.data) {
        throw new Error('Failed to save your profile.')
      }
      return data.data
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: profileKey }),
  })
}

/** Adds an address. Marking it default unsets the prior default for that axis. */
export function useAddAddress() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: Schemas['AddressRequest']): Promise<Address> => {
      const { data, error } = await apiClient.POST('/api/v1/profile/addresses', { body })
      if (error || !data?.data) {
        throw new Error('Failed to add the address.')
      }
      return data.data
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: profileKey }),
  })
}

/** Updates an address the caller owns. */
export function useUpdateAddress() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (vars: { id: string; body: Schemas['AddressRequest'] }): Promise<Address> => {
      const { data, error } = await apiClient.PUT('/api/v1/profile/addresses/{id}', {
        params: { path: { id: vars.id } },
        body: vars.body,
      })
      if (error || !data?.data) {
        throw new Error('Failed to save the address.')
      }
      return data.data
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: profileKey }),
  })
}

/** Deletes an address the caller owns. */
export function useDeleteAddress() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.DELETE('/api/v1/profile/addresses/{id}', {
        params: { path: { id } },
      })
      if (error) {
        throw new Error('Failed to delete the address.')
      }
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: profileKey }),
  })
}
