import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { Skeleton } from '@/components/ui/skeleton'
import { formatCents } from '@/lib/format'
import { ProductImage } from './components/ProductImage'
import { StockBadge } from './components/StockBadge'
import { VariantSelector } from './components/VariantSelector'
import { useProductQuery } from './hooks/useProductQuery'

export function ProductDetailPage() {
  const { slug } = useParams()
  const { data: product, isLoading, isError } = useProductQuery(slug)
  const [selectedVariantId, setSelectedVariantId] = useState('')

  if (isLoading) {
    return <DetailSkeleton />
  }

  if (isError || !product) {
    return (
      <p className="text-sm text-destructive">
        Product not found.{' '}
        <Link to="/" className="underline">
          Back to catalog
        </Link>
      </p>
    )
  }

  const variants = product.variants ?? []
  const selected = variants.find((variant) => variant.id === selectedVariantId) ?? variants[0]

  return (
    <div className="grid gap-8 md:grid-cols-2">
      <ProductImage
        blobKey={product.primaryImageBlobKey}
        alt={product.name ?? 'Product'}
        className="aspect-square w-full rounded-lg"
      />

      <div className="space-y-4">
        {product.brandName ? (
          <p className="text-sm uppercase tracking-wide text-muted-foreground">
            {product.brandName}
          </p>
        ) : null}
        <h1 className="text-3xl font-semibold">{product.name}</h1>

        {selected ? (
          <div className="flex flex-wrap items-center gap-3">
            <span className="text-2xl font-semibold">{formatCents(selected.priceCents ?? 0)}</span>
            {selected.compareAtPriceCents != null &&
            selected.compareAtPriceCents > (selected.priceCents ?? 0) ? (
              <span className="text-muted-foreground line-through">
                {formatCents(selected.compareAtPriceCents)}
              </span>
            ) : null}
            <StockBadge status={selected.stockStatus ?? 'OutOfStock'} />
          </div>
        ) : (
          <p className="text-sm text-muted-foreground">No variants available.</p>
        )}

        {variants.length > 0 ? (
          <div className="space-y-1">
            <span className="text-sm font-medium">Variant</span>
            <VariantSelector
              variants={variants}
              selectedId={selected?.id ?? ''}
              onSelect={setSelectedVariantId}
            />
          </div>
        ) : null}

        {product.description ? (
          <p className="whitespace-pre-line text-sm text-muted-foreground">{product.description}</p>
        ) : null}
      </div>
    </div>
  )
}

function DetailSkeleton() {
  return (
    <div className="grid gap-8 md:grid-cols-2">
      <Skeleton className="aspect-square w-full rounded-lg" />
      <div className="space-y-4">
        <Skeleton className="h-8 w-2/3" />
        <Skeleton className="h-6 w-1/3" />
        <Skeleton className="h-24 w-full" />
      </div>
    </div>
  )
}
