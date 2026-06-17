import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Badge } from '@/components/ui/badge'
import { DataTable, type Column } from '@/components/ui/data-table'
import { EmptyState } from '@/components/ui/empty-state'
import { Input } from '@/components/ui/input'
import { Pagination } from '@/components/ui/pagination'
import { Select } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { OrderStatusBadge } from '@/features/orders/components/OrderStatusBadge'
import type { AdminOrderSummary } from '@/lib/api/types'
import { formatCents } from '@/lib/format'
import { useAdminOrdersQuery } from './hooks/useAdminOrders'

const PAGE_SIZE = 20
const STATUSES = ['Pending', 'Paid', 'Fulfilled', 'Refunding', 'Refunded', 'Cancelled']

/** Admin order workbench list: all orders with status / customer-email filters, composed from the
 *  shared DataTable. Each row links to the detail view where staff fulfil / refund. */
export function AdminOrdersPage() {
  const [page, setPage] = useState(1)
  const [status, setStatus] = useState('')
  const [email, setEmail] = useState('')

  const { data, isLoading, isError } = useAdminOrdersQuery({
    page,
    pageSize: PAGE_SIZE,
    status: status || undefined,
    customerEmail: email || undefined,
  })

  // Changing a filter resets to page 1.
  function setFilter(setter: (value: string) => void, value: string) {
    setter(value)
    setPage(1)
  }

  const columns: Column<AdminOrderSummary>[] = [
    {
      key: 'number',
      header: 'Order',
      cell: (order) => (
        <Link to={`/admin/orders/${order.id}`} className="font-medium text-primary hover:underline">
          #{order.orderNumber}
        </Link>
      ),
    },
    { key: 'date', header: 'Placed', cell: (order) => formatDate(order.placedAt) },
    { key: 'customer', header: 'Customer', cell: (order) => order.customerEmail },
    {
      key: 'status',
      header: 'Status',
      cell: (order) => <OrderStatusBadge status={order.status ?? ''} />,
    },
    {
      key: 'shipment',
      header: 'Shipment',
      cell: (order) =>
        order.shipmentStatus ? (
          <Badge variant="secondary">{order.shipmentStatus}</Badge>
        ) : (
          <span className="text-muted-foreground">—</span>
        ),
    },
    {
      key: 'total',
      header: 'Total',
      className: 'text-right tabular-nums',
      cell: (order) => formatCents(order.totalCents ?? 0),
    },
  ]

  return (
    <section className="space-y-6">
      <h1 className="text-2xl font-semibold tracking-tight">Orders</h1>

      <div className="flex flex-wrap gap-3">
        <Select
          aria-label="Filter by status"
          value={status}
          onChange={(e) => setFilter(setStatus, e.target.value)}
          className="max-w-44"
        >
          <option value="">All statuses</option>
          {STATUSES.map((s) => (
            <option key={s} value={s}>
              {s}
            </option>
          ))}
        </Select>
        <Input
          placeholder="Filter by customer email"
          value={email}
          onChange={(e) => setFilter(setEmail, e.target.value)}
          className="max-w-72"
        />
      </div>

      {isError ? (
        <p className="text-sm text-destructive">Couldn’t load orders. Please try again.</p>
      ) : isLoading ? (
        <div className="space-y-2">
          {Array.from({ length: 6 }).map((_, index) => (
            <Skeleton key={index} className="h-12 w-full" />
          ))}
        </div>
      ) : (
        <>
          <DataTable
            columns={columns}
            rows={data?.items ?? []}
            getRowKey={(order) => order.id ?? ''}
            empty={
              <EmptyState title="No orders" description="No orders match the current filters." />
            }
          />
          {data ? (
            <Pagination
              page={data.page ?? page}
              totalPages={data.totalPages ?? 1}
              hasPrevious={data.hasPrevious ?? false}
              hasNext={data.hasNext ?? false}
              onPageChange={setPage}
            />
          ) : null}
        </>
      )}
    </section>
  )
}

function formatDate(iso: string | undefined): string {
  return iso ? new Date(iso).toLocaleDateString() : ''
}
