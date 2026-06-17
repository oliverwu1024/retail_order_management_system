import { useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { cartKey } from '@/features/cart/hooks/useCartQuery'
import { useOrderBySessionQuery } from '@/features/orders/hooks/useOrdersQuery'

/**
 * Landing page after Stripe redirects back from a successful payment. The order is created
 * server-side by the webhook, which lags the redirect, so we POLL the guest by-session lookup until
 * the order exists before confirming (rather than declaring success blindly). The cart is cleared
 * once the order is confirmed — or once the poll gives up, since payment already succeeded — so the
 * header badge resets.
 */
export function CheckoutSuccessPage() {
  const queryClient = useQueryClient()
  const [params] = useSearchParams()
  const sessionId = params.get('session_id') ?? undefined

  const { data: order, isError } = useOrderBySessionQuery(sessionId)

  // Clear the cart once we KNOW the order landed (or the poll gave up) — never on every poll tick.
  useEffect(() => {
    if (order || isError) {
      void queryClient.invalidateQueries({ queryKey: cartKey })
    }
  }, [order, isError, queryClient])

  return (
    <div className="mx-auto max-w-md space-y-4 py-8 text-center">
      {renderBody()}
      <Button asChild>
        <Link to="/">Continue shopping</Link>
      </Button>
    </div>
  )

  function renderBody() {
    if (!sessionId) {
      return (
        <>
          <h1 className="text-2xl font-semibold">Thank you for your order!</h1>
          <p className="text-muted-foreground">Your order is being processed.</p>
        </>
      )
    }
    if (order) {
      return (
        <>
          <h1 className="text-2xl font-semibold">Thank you for your order!</h1>
          <p className="text-muted-foreground">
            Order #{order.orderNumber} is confirmed and a receipt is on its way.
          </p>
        </>
      )
    }
    if (isError) {
      // Poll exhausted (webhook genuinely slow/failed) — payment still succeeded, so reassure.
      return (
        <>
          <h1 className="text-2xl font-semibold">Payment received</h1>
          <p className="text-muted-foreground">
            Your order is still finalising — it will appear under My Orders shortly.
          </p>
        </>
      )
    }
    // Pending / between polls: the webhook hasn't created the order yet.
    return (
      <>
        <h1 className="text-2xl font-semibold">Finalising your order…</h1>
        <p className="text-muted-foreground">
          Your payment was received — we&apos;re confirming your order. This only takes a moment.
        </p>
      </>
    )
  }
}
