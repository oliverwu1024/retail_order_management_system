import { Link, useParams } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { toast } from '@/hooks/use-toast'
import { formatCents } from '@/lib/format'
import { OrderStatusBadge } from './components/OrderStatusBadge'
import { useCancelOrder } from './hooks/useOrderMutations'
import { useOrderQuery } from './hooks/useOrdersQuery'

export function OrderDetailPage() {
  const { id } = useParams()
  const { data: order, isLoading, isError } = useOrderQuery(id)
  const cancelOrder = useCancelOrder()

  if (isLoading) {
    return <Skeleton className="h-64 w-full" />
  }

  if (isError || !order) {
    return (
      <p className="text-sm text-destructive">
        Order not found.{' '}
        <Link to="/orders" className="underline">
          Back to orders
        </Link>
      </p>
    )
  }

  const canCancel = order.status === 'Paid'
  const shipping = order.shippingAddress

  function onCancel() {
    if (!id) {
      return
    }
    cancelOrder.mutate(id, {
      onSuccess: () =>
        toast({ title: 'Order cancelled', description: 'Your refund is on its way.' }),
      onError: (error) =>
        toast({
          variant: 'destructive',
          title: 'Could not cancel the order',
          description: error instanceof Error ? error.message : 'Please try again.',
        }),
    })
  }

  return (
    <div className="mx-auto max-w-2xl space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Order #{order.orderNumber}</h1>
        <OrderStatusBadge status={order.status ?? ''} />
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Items</CardTitle>
        </CardHeader>
        <CardContent className="space-y-2 text-sm">
          {(order.lines ?? []).map((line, index) => (
            <div key={`${line.sku}-${index}`} className="flex justify-between">
              <span>
                {line.productName} × {line.quantity}
              </span>
              <span className="tabular-nums">{formatCents(line.lineTotalCents ?? 0)}</span>
            </div>
          ))}
          <div className="space-y-1 border-t pt-2">
            <TotalRow label="Subtotal" cents={order.subtotalCents ?? 0} />
            <TotalRow label="Tax" cents={order.taxCents ?? 0} />
            <TotalRow label="Shipping" cents={order.shippingCents ?? 0} />
            <div className="flex justify-between font-semibold">
              <span>Total</span>
              <span className="tabular-nums">{formatCents(order.totalCents ?? 0)}</span>
            </div>
          </div>
        </CardContent>
      </Card>

      {shipping ? (
        <Card>
          <CardHeader>
            <CardTitle>Shipping to</CardTitle>
          </CardHeader>
          <CardContent className="text-sm text-muted-foreground">
            {shipping.recipientName ? <p>{shipping.recipientName}</p> : null}
            <p>{shipping.line1}</p>
            {shipping.line2 ? <p>{shipping.line2}</p> : null}
            <p>
              {shipping.city}
              {shipping.region ? `, ${shipping.region}` : ''} {shipping.postalCode}
            </p>
            <p>{shipping.country}</p>
          </CardContent>
        </Card>
      ) : null}

      <div className="flex items-center justify-between">
        <Button variant="outline" asChild>
          <Link to="/orders">Back to orders</Link>
        </Button>
        {canCancel ? (
          <Button variant="destructive" disabled={cancelOrder.isPending} onClick={onCancel}>
            {cancelOrder.isPending ? 'Cancelling…' : 'Cancel order'}
          </Button>
        ) : null}
      </div>
    </div>
  )
}

function TotalRow({ label, cents }: { label: string; cents: number }) {
  return (
    <div className="flex justify-between text-muted-foreground">
      <span>{label}</span>
      <span className="tabular-nums">{formatCents(cents)}</span>
    </div>
  )
}
