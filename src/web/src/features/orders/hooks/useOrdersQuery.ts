import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { OrderDetail, OrderPage } from '@/lib/api/types'

/** Error that preserves the HTTP status so the UI can tell 404 / 401 / network apart. */
export class ApiError extends Error {
  readonly status: number | undefined

  constructor(message: string, status: number | undefined) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

/** Query keys for the customer's orders (list + detail + guest by-session lookup). */
export const orderKeys = {
  all: ['orders'] as const,
  list: (page: number) => ['orders', 'list', page] as const,
  detail: (id: string) => ['orders', 'detail', id] as const,
  bySession: (sessionId: string) => ['orders', 'by-session', sessionId] as const,
}

/** The current customer's orders (paged, newest first). */
export function useOrdersQuery(page: number, pageSize = 20) {
  return useQuery({
    queryKey: orderKeys.list(page),
    queryFn: async (): Promise<OrderPage> => {
      const { data, error } = await apiClient.GET('/api/v1/orders', {
        // PascalCase to match the OrderListQuery DTO (consistent with the catalogue listing).
        params: { query: { Page: page, PageSize: pageSize } },
      })
      if (error || !data?.data) {
        throw new Error('Failed to load your orders.')
      }
      return data.data
    },
  })
}

/** One of the customer's orders by id (404 if not theirs). Preserves the HTTP status on failure. */
export function useOrderQuery(id: string | undefined) {
  return useQuery({
    queryKey: orderKeys.detail(id ?? ''),
    enabled: Boolean(id),
    queryFn: async (): Promise<OrderDetail> => {
      const { data, error, response } = await apiClient.GET('/api/v1/orders/{id}', {
        params: { path: { id: id! } },
      })
      if (error || !data?.data) {
        // response is undefined on a network failure; otherwise it carries 404 / 401.
        throw new ApiError('Failed to load the order.', response?.status)
      }
      return data.data
    },
    // A definitive 404 / 401 won't change on retry; only retry transient/network failures.
    retry: (count, err) =>
      err instanceof ApiError && (err.status === 404 || err.status === 401) ? false : count < 2,
  })
}

/**
 * Polls the guest by-session lookup until the Stripe webhook has created the order. The order is
 * materialised out-of-band by the webhook, so the lookup 404s for a moment after the redirect — we
 * treat that as "not ready yet" and re-poll, giving the webhook a budget of ~10 × 1.5s before
 * giving up (the caller then shows a soft "still finalising" fallback).
 */
export function useOrderBySessionQuery(sessionId: string | undefined) {
  return useQuery({
    queryKey: orderKeys.bySession(sessionId ?? ''),
    enabled: Boolean(sessionId),
    queryFn: async (): Promise<OrderDetail> => {
      const { data, error, response } = await apiClient.GET(
        '/api/v1/orders/by-session/{sessionId}',
        {
          params: { path: { sessionId: sessionId! } },
        },
      )
      if (error || !data?.data) {
        throw new ApiError('Order not ready yet.', response?.status)
      }
      return data.data
    },
    // Keep re-polling only while it's still a 404 (webhook hasn't run); cap the wait.
    retry: (count, err) => err instanceof ApiError && err.status === 404 && count < 10,
    retryDelay: 1500,
  })
}
