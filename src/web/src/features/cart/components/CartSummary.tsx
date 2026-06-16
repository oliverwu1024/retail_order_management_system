import { Button } from '@/components/ui/button'
import { Card, CardContent, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'
import { formatCents } from '@/lib/format'

interface CartSummaryProps {
  subtotalCents: number
  totalQuantity: number
  onCheckout: () => void
  isCheckingOut: boolean
}

/** Cart totals + the checkout call-to-action. */
export function CartSummary({
  subtotalCents,
  totalQuantity,
  onCheckout,
  isCheckingOut,
}: CartSummaryProps) {
  return (
    <Card className="h-fit">
      <CardHeader>
        <CardTitle>Order summary</CardTitle>
      </CardHeader>
      <CardContent className="space-y-2 text-sm">
        <div className="flex justify-between">
          <span className="text-muted-foreground">Items</span>
          <span className="tabular-nums">{totalQuantity}</span>
        </div>
        <div className="flex justify-between text-base font-semibold">
          <span>Subtotal</span>
          <span className="tabular-nums">{formatCents(subtotalCents)}</span>
        </div>
        <p className="text-xs text-muted-foreground">
          Tax and shipping are calculated at checkout.
        </p>
      </CardContent>
      <CardFooter>
        {/* Redirects to Stripe's hosted checkout (the page does window.location.assign). */}
        <Button className="w-full" onClick={onCheckout} disabled={isCheckingOut}>
          {isCheckingOut ? 'Starting checkout…' : 'Proceed to checkout'}
        </Button>
      </CardFooter>
    </Card>
  )
}
