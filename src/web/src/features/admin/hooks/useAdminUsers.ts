import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { components } from '@/lib/api/schema'
import type { AdminUser, AdminUserPage } from '@/lib/api/types'

type Schemas = components['schemas']

export interface AdminUserListParams {
  page: number
  pageSize: number
  role?: string
}

/** Query keys for the admin user cache — the create mutation invalidates ['admin','users'] to refetch. */
export const adminUserKeys = {
  all: ['admin', 'users'] as const,
  list: (params: AdminUserListParams) => ['admin', 'users', 'list', params] as const,
}

/** Paged back-office account list (StoreManager + Administrator). */
export function useAdminUsersQuery(params: AdminUserListParams) {
  return useQuery({
    queryKey: adminUserKeys.list(params),
    queryFn: async (): Promise<AdminUserPage> => {
      const { data, error } = await apiClient.GET('/api/v1/admin/users', {
        // PascalCase query params: ASP.NET binds by property name, not the camelCase JSON policy.
        params: { query: { Role: params.role, Page: params.page, PageSize: params.pageSize } },
      })
      if (error || !data?.data) {
        throw new Error('Failed to load users.')
      }
      return data.data
    },
  })
}

/** Creates a Staff/StoreManager account, then invalidates the user list. */
export function useCreateUser() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: Schemas['CreateUserRequest']): Promise<AdminUser> => {
      const { data, error } = await apiClient.POST('/api/v1/admin/users', { body })
      if (error || !data?.data) {
        // Surface the server's message when present (e.g. 409 email-taken, or the 403 a StoreManager
        // gets trying to create a StoreManager) so the toast is actionable.
        throw new Error(serverMessage(error) ?? 'Failed to create the account.')
      }
      return data.data
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: adminUserKeys.all })
    },
  })
}

/** Pulls the ApiResponse envelope's `message` off an openapi-fetch error body, if present. */
function serverMessage(error: unknown): string | undefined {
  if (error && typeof error === 'object' && 'message' in error) {
    const message = (error as { message?: unknown }).message
    return typeof message === 'string' ? message : undefined
  }
  return undefined
}
