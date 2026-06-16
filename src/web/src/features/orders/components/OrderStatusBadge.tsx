import { Badge } from '@/components/ui/badge'

type BadgeVariant = 'default' | 'secondary' | 'success' | 'warning' | 'destructive'

// Order status → badge colour. Falls back to neutral for anything unmapped.
const VARIANT: Record<string, BadgeVariant> = {
  Pending: 'warning',
  Paid: 'success',
  Fulfilled: 'default',
  Cancelled: 'secondary',
  Refunded: 'secondary',
}

export function OrderStatusBadge({ status }: { status: string }) {
  return <Badge variant={VARIANT[status] ?? 'secondary'}>{status}</Badge>
}
