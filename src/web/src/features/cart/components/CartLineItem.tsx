import { Link } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { ProductImage } from '@/features/storefront/components/ProductImage'
import type { CartItem } from '@/lib/api/types'
import { formatCents } from '@/lib/format'

interface CartLineItemProps {
  item: CartItem
  busy: boolean
  onChangeQuantity: (quantity: number) => void
  onRemove: () => void
}

const MAX_QUANTITY = 99

/** One cart line: thumbnail, name + options, a quantity stepper, line total, and remove. */
export function CartLineItem({ item, busy, onChangeQuantity, onRemove }: CartLineItemProps) {
  const quantity = item.quantity ?? 0
  const options = Object.entries(item.options ?? {})
  const slug = item.productSlug ?? ''

  return (
    <div className="flex gap-4 border-b py-4">
      <Link to={`/products/${slug}`} className="shrink-0">
        <ProductImage
          blobKey={item.primaryImageBlobKey}
          alt={item.productName ?? 'Product'}
          className="h-20 w-20 rounded-md"
        />
      </Link>

      <div className="flex flex-1 flex-col gap-1">
        <Link to={`/products/${slug}`} className="font-medium hover:underline">
          {item.productName}
        </Link>
        {options.length > 0 ? (
          <p className="text-xs text-muted-foreground">
            {options.map(([key, value]) => `${key}: ${value}`).join(' · ')}
          </p>
        ) : null}
        <p className="text-sm text-muted-foreground">
          {formatCents(item.unitPriceCents ?? 0)} each
        </p>
        {item.inStock === false ? (
          <p className="text-xs text-destructive">Not enough stock for this quantity.</p>
        ) : null}

        <div className="mt-1 flex items-center gap-2">
          <Button
            variant="outline"
            size="icon"
            disabled={busy || quantity <= 1}
            onClick={() => onChangeQuantity(quantity - 1)}
            aria-label="Decrease quantity"
          >
            −
          </Button>
          <span className="w-8 text-center text-sm tabular-nums">{quantity}</span>
          <Button
            variant="outline"
            size="icon"
            disabled={busy || quantity >= MAX_QUANTITY}
            onClick={() => onChangeQuantity(quantity + 1)}
            aria-label="Increase quantity"
          >
            +
          </Button>
          <Button
            variant="ghost"
            size="sm"
            className="ml-2 text-destructive"
            disabled={busy}
            onClick={onRemove}
          >
            Remove
          </Button>
        </div>
      </div>

      <div className="text-right font-medium">{formatCents(item.lineTotalCents ?? 0)}</div>
    </div>
  )
}
