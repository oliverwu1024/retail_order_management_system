import { useMutation } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'

/**
 * Starts checkout: the backend reserves the cart's stock and creates a Stripe Checkout Session,
 * returning the hosted-checkout URL. The caller redirects the browser there
 * (`window.location.assign`). A 409 means the cart is empty or out of stock.
 */
export function useStartCheckout() {
  return useMutation({
    mutationFn: async (): Promise<string> => {
      const { data, error } = await apiClient.POST('/api/v1/orders/checkout-session', {
        // The SPA's origin, so the backend can build the Stripe success/cancel return URLs.
        body: { returnBaseUrl: window.location.origin },
      })
      if (error || !data?.data?.url) {
        throw new Error('Failed to start checkout.')
      }
      return data.data.url
    },
  })
}
