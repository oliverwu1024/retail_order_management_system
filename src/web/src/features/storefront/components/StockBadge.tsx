import { Badge, type BadgeProps } from '@/components/ui/badge'

const STATUS_META: Record<string, { label: string; variant: BadgeProps['variant'] }> = {
  InStock: { label: 'In stock', variant: 'success' },
  LowStock: { label: 'Low stock', variant: 'warning' },
  OutOfStock: { label: 'Out of stock', variant: 'destructive' },
}

/** Maps the API stock-status string to a coloured badge. */
export function StockBadge({ status }: { status: string }) {
  const meta = STATUS_META[status] ?? { label: status, variant: 'secondary' }
  return <Badge variant={meta.variant}>{meta.label}</Badge>
}
