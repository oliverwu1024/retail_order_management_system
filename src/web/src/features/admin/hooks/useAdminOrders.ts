import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { components } from '@/lib/api/schema'
import type { AdminOrderDetail, AdminOrderPage } from '@/lib/api/types'

type Schemas = components['schemas']

export interface AdminOrderListParams {
  page: number
  pageSize: number
  status?: string
  customerEmail?: string
}

/** Query keys for the admin order workbench — write actions invalidate ['admin','orders']. */
export const adminOrderKeys = {
  all: ['admin', 'orders'] as const,
  list: (params: AdminOrderListParams) => ['admin', 'orders', 'list', params] as const,
  detail: (id: string) => ['admin', 'orders', 'detail', id] as const,
}

/** All orders (paged) with optional status / customer-email filters. */
export function useAdminOrdersQuery(params: AdminOrderListParams) {
  return useQuery({
    queryKey: adminOrderKeys.list(params),
    queryFn: async (): Promise<AdminOrderPage> => {
      const { data, error } = await apiClient.GET('/api/v1/admin/orders', {
        // PascalCase query params (ASP.NET binds by property name, not the camelCase JSON policy).
        params: {
          query: {
            Status: params.status,
            CustomerEmail: params.customerEmail,
            Page: params.page,
            PageSize: params.pageSize,
          },
        },
      })
      if (error || !data?.data) {
        throw new Error('Failed to load orders.')
      }
      return data.data
    },
  })
}

/** One order's full admin detail (payments + shipment). */
export function useAdminOrderQuery(id: string | undefined) {
  return useQuery({
    queryKey: adminOrderKeys.detail(id ?? ''),
    enabled: Boolean(id),
    queryFn: async (): Promise<AdminOrderDetail> => {
      const { data, error } = await apiClient.GET('/api/v1/admin/orders/{id}', {
        params: { path: { id: id! } },
      })
      if (error || !data?.data) {
        throw new Error('Failed to load the order.')
      }
      return data.data
    },
  })
}

/** "Mark as Shipped" (carrier + tracking → Shipment, order → Fulfilled). */
export function useMarkShipped() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (vars: {
      id: string
      body: Schemas['MarkShippedRequest']
    }): Promise<AdminOrderDetail> => {
      const { data, error } = await apiClient.POST('/api/v1/admin/orders/{id}/ship', {
        params: { path: { id: vars.id } },
        body: vars.body,
      })
      if (error || !data?.data) {
        throw new Error(serverMessage(error) ?? 'Failed to mark the order shipped.')
      }
      return data.data
    },
    onSuccess: (order, vars) => applyOrderUpdate(queryClient, vars.id, order),
  })
}

/** "Mark as Delivered" (advance the shipment Shipped → Delivered). */
export function useMarkDelivered() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (vars: { id: string }): Promise<AdminOrderDetail> => {
      const { data, error } = await apiClient.POST('/api/v1/admin/orders/{id}/deliver', {
        params: { path: { id: vars.id } },
      })
      if (error || !data?.data) {
        throw new Error(serverMessage(error) ?? 'Failed to mark the order delivered.')
      }
      return data.data
    },
    onSuccess: (order, vars) => applyOrderUpdate(queryClient, vars.id, order),
  })
}

/** Admin full refund (StoreManager + Administrator). */
export function useRefundOrder() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (vars: { id: string }): Promise<AdminOrderDetail> => {
      const { data, error } = await apiClient.POST('/api/v1/admin/orders/{id}/refund', {
        params: { path: { id: vars.id } },
      })
      if (error || !data?.data) {
        throw new Error(serverMessage(error) ?? 'Failed to refund the order.')
      }
      return data.data
    },
    onSuccess: (order, vars) => applyOrderUpdate(queryClient, vars.id, order),
  })
}

// Write the authoritative updated order into the detail cache and refresh the list namespace.
function applyOrderUpdate(
  queryClient: ReturnType<typeof useQueryClient>,
  id: string,
  order: AdminOrderDetail,
) {
  queryClient.setQueryData(adminOrderKeys.detail(id), order)
  void queryClient.invalidateQueries({ queryKey: adminOrderKeys.all })
}

/** Pulls the ApiResponse envelope's `message` off an openapi-fetch error body, if present. */
function serverMessage(error: unknown): string | undefined {
  if (error && typeof error === 'object' && 'message' in error) {
    const message = (error as { message?: unknown }).message
    return typeof message === 'string' ? message : undefined
  }
  return undefined
}
