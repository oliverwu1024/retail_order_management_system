import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Pagination } from '@/components/ui/pagination'
import { Skeleton } from '@/components/ui/skeleton'
import { formatCents } from '@/lib/format'
import { OrderStatusBadge } from './components/OrderStatusBadge'
import { useOrdersQuery } from './hooks/useOrdersQuery'

export function OrdersPage() {
  const [page, setPage] = useState(1)
  const { data, isLoading, isError } = useOrdersQuery(page)

  if (isLoading) {
    return <Skeleton className="h-40 w-full" />
  }

  if (isError) {
    return <p className="text-sm text-destructive">Failed to load your orders. Please refresh.</p>
  }

  const items = data?.items ?? []

  if (items.length === 0) {
    return (
      <div className="space-y-4">
        <h1 className="text-2xl font-semibold">Your orders</h1>
        <p className="text-muted-foreground">You have no orders yet.</p>
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">Your orders</h1>

      <div className="divide-y rounded-md border">
        {items.map((order) => (
          <Link
            key={order.id}
            to={`/orders/${order.id}`}
            className="flex items-center justify-between gap-4 px-4 py-3 hover:bg-muted/50"
          >
            <div>
              <p className="font-medium">Order #{order.orderNumber}</p>
              <p className="text-xs text-muted-foreground">
                {formatDate(order.placedAt)} · {order.itemCount} item(s)
              </p>
            </div>
            <div className="flex items-center gap-3">
              <OrderStatusBadge status={order.status ?? ''} />
              <span className="font-medium tabular-nums">{formatCents(order.totalCents ?? 0)}</span>
            </div>
          </Link>
        ))}
      </div>

      <Pagination
        page={data?.page ?? 1}
        totalPages={data?.totalPages ?? 1}
        hasPrevious={data?.hasPrevious ?? false}
        hasNext={data?.hasNext ?? false}
        onPageChange={setPage}
      />
    </div>
  )
}

function formatDate(iso: string | undefined): string {
  return iso ? new Date(iso).toLocaleDateString() : ''
}
