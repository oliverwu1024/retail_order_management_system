import { useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { cartKey } from '@/features/cart/hooks/useCartQuery'

/**
 * Landing page after Stripe redirects back from a successful payment. The order itself is
 * created server-side by the webhook (which may lag a moment), so this page just confirms
 * receipt and refreshes the cached cart (the webhook converted it) so the header badge clears.
 * Order details live under "My Orders" (Chunk 4).
 */
export function CheckoutSuccessPage() {
  const queryClient = useQueryClient()
  const [params] = useSearchParams()
  const hasSession = params.get('session_id') !== null

  useEffect(() => {
    void queryClient.invalidateQueries({ queryKey: cartKey })
  }, [queryClient])

  return (
    <div className="mx-auto max-w-md space-y-4 py-8 text-center">
      <h1 className="text-2xl font-semibold">Thank you for your order!</h1>
      <p className="text-muted-foreground">
        {hasSession
          ? 'Your payment was received and your order is being finalized. A receipt is on its way.'
          : 'Your order is being processed.'}
      </p>
      <Button asChild>
        <Link to="/">Continue shopping</Link>
      </Button>
    </div>
  )
}
