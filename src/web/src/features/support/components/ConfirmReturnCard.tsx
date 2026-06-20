import { Button } from '@/components/ui/button'
import type { ChatProposedAction } from '@/lib/api/types'
import { formatCents } from '@/lib/format'

interface ConfirmReturnCardProps {
  action: ChatProposedAction
  onConfirm: () => void
  onDismiss: () => void
  isConfirming: boolean
}

/**
 * The confirmation gate for a chat-proposed refund (Phase 5A, Chunk 3). Presentational only — the
 * actual cancel mutation is owned by {@link ChatDrawer} (so it survives this card unmounting and can
 * disable the composer while in flight). The assistant never cancels an order on its own; only an
 * explicit Confirm here runs the EXISTING customer cancel flow (`POST /orders/{id}/cancel`).
 */
export function ConfirmReturnCard({
  action,
  onConfirm,
  onDismiss,
  isConfirming,
}: ConfirmReturnCardProps) {
  const refund = formatCents(action.refundAmountCents ?? 0)
  return (
    <div className="rounded-lg border border-primary/40 bg-background p-3 text-sm">
      <p className="font-medium">Cancel order #{action.orderNumber}?</p>
      <p className="text-muted-foreground">You’ll be refunded {refund}.</p>
      <div className="mt-2 flex gap-2">
        <Button size="sm" onClick={onConfirm} disabled={isConfirming}>
          {isConfirming ? 'Cancelling…' : 'Confirm refund'}
        </Button>
        <Button size="sm" variant="outline" onClick={onDismiss} disabled={isConfirming}>
          Keep order
        </Button>
      </div>
    </div>
  )
}
