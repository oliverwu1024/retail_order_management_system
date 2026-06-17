import { useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { Modal } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { toast } from '@/hooks/use-toast'
import { OrderStatusBadge } from '@/features/orders/components/OrderStatusBadge'
import type { AdminOrderDetail } from '@/lib/api/types'
import { ROLE_SETS, hasAnyRole } from '@/lib/auth/roleSets'
import { formatCents } from '@/lib/format'
import { useAuthStore } from '@/lib/store/auth-store'
import {
  useAdminOrderQuery,
  useMarkDelivered,
  useMarkShipped,
  useRefundOrder,
} from './hooks/useAdminOrders'

/** Admin order detail: customer, line items, money breakdown, payment ledger, shipment — plus the
 *  fulfilment (ship/deliver) and refund actions, gated by role and order state. */
export function AdminOrderDetailPage() {
  const { id } = useParams<{ id: string }>()
  const { data: order, isLoading, isError } = useAdminOrderQuery(id)
  const roles = useAuthStore((state) => state.user?.roles)
  const canRefund = hasAnyRole(roles, ROLE_SETS.refund)

  if (isLoading) {
    return <Skeleton className="h-64 w-full" />
  }
  if (isError || !order) {
    return <p className="text-sm text-destructive">Couldn’t load this order. Please try again.</p>
  }

  return (
    <section className="space-y-6">
      <div className="flex items-start justify-between gap-4">
        <div>
          <Link to="/admin/orders" className="text-sm text-muted-foreground hover:text-foreground">
            ← Orders
          </Link>
          <h1 className="mt-1 text-2xl font-semibold tracking-tight">Order #{order.orderNumber}</h1>
          <p className="text-sm text-muted-foreground">
            {order.customerEmail} · {formatDate(order.placedAt)}
          </p>
        </div>
        <OrderStatusBadge status={order.status ?? ''} />
      </div>

      <OrderActions order={order} canRefund={canRefund} />

      <div className="grid gap-6 lg:grid-cols-3">
        <div className="space-y-6 lg:col-span-2">
          <Panel title="Items">
            <table className="w-full text-sm" aria-label="Order items">
              <thead className="border-b text-left text-muted-foreground">
                <tr>
                  <th scope="col" className="py-2 font-medium">
                    Item
                  </th>
                  <th scope="col" className="py-2 font-medium">
                    Qty
                  </th>
                  <th scope="col" className="py-2 text-right font-medium">
                    Total
                  </th>
                </tr>
              </thead>
              <tbody>
                {(order.lines ?? []).map((line, index) => (
                  <tr key={`${line.sku}-${index}`} className="border-b last:border-0">
                    <td className="py-2">
                      <p>{line.productName}</p>
                      <p className="font-mono text-xs text-muted-foreground">{line.sku}</p>
                    </td>
                    <td className="py-2">{line.quantity}</td>
                    <td className="py-2 text-right tabular-nums">
                      {formatCents(line.lineTotalCents ?? 0)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            <dl className="mt-4 space-y-1 text-sm">
              <Row label="Subtotal" value={formatCents(order.subtotalCents ?? 0)} />
              <Row label="Tax" value={formatCents(order.taxCents ?? 0)} />
              <Row label="Shipping" value={formatCents(order.shippingCents ?? 0)} />
              <Row label="Total" value={formatCents(order.totalCents ?? 0)} strong />
            </dl>
          </Panel>

          <Panel title="Payments">
            {(order.payments ?? []).length === 0 ? (
              <p className="text-sm text-muted-foreground">No payments recorded.</p>
            ) : (
              <ul className="space-y-2 text-sm">
                {(order.payments ?? []).map((payment, index) => (
                  <li key={index} className="flex items-center justify-between">
                    <span className="text-muted-foreground">
                      {payment.status} · {formatDate(payment.createdAt)}
                    </span>
                    <span className="tabular-nums">{formatCents(payment.amountCents ?? 0)}</span>
                  </li>
                ))}
              </ul>
            )}
          </Panel>
        </div>

        <div className="space-y-6">
          <Panel title="Shipment">
            {order.shipment ? (
              <dl className="space-y-1 text-sm">
                <Row label="Status" value={order.shipment.status ?? '—'} />
                <Row label="Carrier" value={order.shipment.carrier ?? '—'} />
                <Row label="Tracking" value={order.shipment.trackingNumber ?? '—'} />
                {order.shipment.shippedAt ? (
                  <Row label="Shipped" value={formatDate(order.shipment.shippedAt)} />
                ) : null}
                {order.shipment.deliveredAt ? (
                  <Row label="Delivered" value={formatDate(order.shipment.deliveredAt)} />
                ) : null}
              </dl>
            ) : (
              <p className="text-sm text-muted-foreground">Not shipped yet.</p>
            )}
          </Panel>

          <Panel title="Shipping address">
            <address className="text-sm not-italic text-muted-foreground">
              {order.shippingAddress?.recipientName ? (
                <>
                  {order.shippingAddress.recipientName}
                  <br />
                </>
              ) : null}
              {order.shippingAddress?.line1}
              <br />
              {order.shippingAddress?.city} {order.shippingAddress?.postalCode}
              <br />
              {order.shippingAddress?.country}
            </address>
          </Panel>
        </div>
      </div>
    </section>
  )
}

function OrderActions({ order, canRefund }: { order: AdminOrderDetail; canRefund: boolean }) {
  const id = order.id ?? ''
  const markDelivered = useMarkDelivered()
  const [shipOpen, setShipOpen] = useState(false)
  const [refundOpen, setRefundOpen] = useState(false)

  const status = order.status
  const shipmentStatus = order.shipment?.status

  function notifyError(error: unknown) {
    toast({
      variant: 'destructive',
      title: 'Action failed',
      description: error instanceof Error ? error.message : undefined,
    })
  }

  const busy = markDelivered.isPending

  return (
    <div className="flex flex-wrap gap-2">
      {status === 'Paid' ? <Button onClick={() => setShipOpen(true)}>Mark shipped</Button> : null}

      {status === 'Fulfilled' && shipmentStatus === 'Shipped' ? (
        <Button
          variant="outline"
          disabled={busy}
          onClick={() =>
            markDelivered.mutate(
              { id },
              { onSuccess: () => toast({ title: 'Marked delivered' }), onError: notifyError },
            )
          }
        >
          Mark delivered
        </Button>
      ) : null}

      {canRefund && status === 'Paid' ? (
        <Button variant="destructive" onClick={() => setRefundOpen(true)}>
          Refund
        </Button>
      ) : null}

      <MarkShippedModal
        id={id}
        orderNumber={order.orderNumber ?? 0}
        open={shipOpen}
        onOpenChange={setShipOpen}
      />
      <RefundModal
        id={id}
        orderNumber={order.orderNumber ?? 0}
        totalCents={order.totalCents ?? 0}
        open={refundOpen}
        onOpenChange={setRefundOpen}
      />
    </div>
  )
}

function RefundModal({
  id,
  orderNumber,
  totalCents,
  open,
  onOpenChange,
}: {
  id: string
  orderNumber: number
  totalCents: number
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  const refund = useRefundOrder()

  function onConfirm() {
    refund.mutate(
      { id },
      {
        onSuccess: () => {
          toast({ title: 'Order refunded' })
          onOpenChange(false)
        },
        onError: (error) =>
          toast({
            variant: 'destructive',
            title: 'Couldn’t refund',
            description: error instanceof Error ? error.message : undefined,
          }),
      },
    )
  }

  return (
    <Modal
      open={open}
      onOpenChange={onOpenChange}
      title={`Refund order #${orderNumber}`}
      description={`This refunds the customer ${formatCents(totalCents)} and restocks the items. This can’t be undone.`}
    >
      <div className="flex justify-end gap-2">
        <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
          Cancel
        </Button>
        <Button type="button" variant="destructive" disabled={refund.isPending} onClick={onConfirm}>
          {refund.isPending ? 'Refunding…' : `Refund ${formatCents(totalCents)}`}
        </Button>
      </div>
    </Modal>
  )
}

function MarkShippedModal({
  id,
  orderNumber,
  open,
  onOpenChange,
}: {
  id: string
  orderNumber: number
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  const markShipped = useMarkShipped()
  const [carrier, setCarrier] = useState('')
  const [trackingNumber, setTrackingNumber] = useState('')

  function onSubmit(event: FormEvent) {
    event.preventDefault()
    markShipped.mutate(
      { id, body: { carrier, trackingNumber } },
      {
        onSuccess: () => {
          toast({ title: 'Marked shipped' })
          setCarrier('')
          setTrackingNumber('')
          onOpenChange(false)
        },
        onError: (error) =>
          toast({
            variant: 'destructive',
            title: 'Couldn’t mark shipped',
            description: error instanceof Error ? error.message : undefined,
          }),
      },
    )
  }

  return (
    <Modal
      open={open}
      onOpenChange={onOpenChange}
      title={`Ship order #${orderNumber}`}
      description="Enter the carrier and tracking number; the order moves to Fulfilled."
    >
      <form onSubmit={onSubmit} className="space-y-4">
        <div className="space-y-1">
          <label htmlFor="ship-carrier" className="text-xs font-medium">
            Carrier
          </label>
          <Input
            id="ship-carrier"
            value={carrier}
            onChange={(e) => setCarrier(e.target.value)}
            required
          />
        </div>
        <div className="space-y-1">
          <label htmlFor="ship-tracking" className="text-xs font-medium">
            Tracking number
          </label>
          <Input
            id="ship-tracking"
            value={trackingNumber}
            onChange={(e) => setTrackingNumber(e.target.value)}
            required
          />
        </div>
        <div className="flex justify-end gap-2">
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button type="submit" disabled={markShipped.isPending}>
            {markShipped.isPending ? 'Shipping…' : 'Mark shipped'}
          </Button>
        </div>
      </form>
    </Modal>
  )
}

function Panel({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="rounded-md border p-4">
      <h2 className="mb-3 text-sm font-semibold">{title}</h2>
      {children}
    </div>
  )
}

function Row({ label, value, strong }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className="flex justify-between">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className={strong ? 'font-semibold tabular-nums' : 'tabular-nums'}>{value}</dd>
    </div>
  )
}

function formatDate(iso: string | undefined): string {
  return iso ? new Date(iso).toLocaleDateString() : ''
}
