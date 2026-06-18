import type { ProductSentiment } from '@/lib/api/types'

/** Products whose average sentiment is below the attention threshold (avg < −0.2), worst-first. */
export function ProductsNeedingAttention({ products }: { products: ProductSentiment[] }) {
  if (products.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        No products need attention — sentiment is healthy.
      </p>
    )
  }

  return (
    <ul className="divide-y text-sm">
      {products.map((product) => (
        <li key={product.productId} className="flex items-center justify-between gap-3 py-2">
          <span className="font-medium">{product.productName}</span>
          <span className="flex items-center gap-3 text-muted-foreground">
            <span className="rounded-full bg-red-100 px-2 py-0.5 text-xs font-medium tabular-nums text-red-800">
              {(product.averageScore ?? 0).toFixed(2)}
            </span>
            <span className="tabular-nums">
              {product.reviewCount} {product.reviewCount === 1 ? 'review' : 'reviews'}
            </span>
          </span>
        </li>
      ))}
    </ul>
  )
}
