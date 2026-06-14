import { Link } from 'react-router-dom'
import { Card, CardContent } from '@/components/ui/card'
import { formatCents } from '@/lib/format'
import type { ProductSummary } from '@/lib/api/types'
import { ProductImage } from './ProductImage'

/** Catalogue grid tile — links to the product detail page. */
export function ProductCard({ product }: { product: ProductSummary }) {
  return (
    <Link to={`/products/${product.slug}`} className="group block">
      <Card className="overflow-hidden transition-shadow hover:shadow-md">
        <ProductImage
          blobKey={product.primaryImageBlobKey}
          alt={product.name ?? 'Product'}
          className="aspect-square w-full"
        />
        <CardContent className="p-4">
          {product.brandName ? (
            <p className="text-xs uppercase tracking-wide text-muted-foreground">
              {product.brandName}
            </p>
          ) : null}
          <h3 className="line-clamp-2 font-medium group-hover:underline">{product.name}</h3>
          <p className="mt-2 text-sm">
            {product.fromPriceCents != null
              ? `From ${formatCents(product.fromPriceCents)}`
              : 'Currently unavailable'}
          </p>
        </CardContent>
      </Card>
    </Link>
  )
}
