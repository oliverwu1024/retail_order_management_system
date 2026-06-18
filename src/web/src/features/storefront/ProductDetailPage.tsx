import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { Select } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { useAddToCart } from '@/features/cart/hooks/useCartMutations'
import { toast } from '@/hooks/use-toast'
import { formatCents } from '@/lib/format'
import { ProductGallery } from './components/ProductGallery'
import { ProductReviews } from './components/ProductReviews'
import { StockBadge } from './components/StockBadge'
import { VariantSelector } from './components/VariantSelector'
import { useProductQuery } from './hooks/useProductQuery'

export function ProductDetailPage() {
  const { slug } = useParams()
  const { data: product, isLoading, isError } = useProductQuery(slug)
  const [selectedVariantId, setSelectedVariantId] = useState('')
  const [quantity, setQuantity] = useState(1)
  const addToCart = useAddToCart()

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
  const canAddToCart = selected != null && selected.stockStatus !== 'OutOfStock'
  // Capture the name in this narrowed scope — `product` is non-undefined here, but TS
  // can't carry that narrowing into the onAddToCart closure below.
  const productName = product.name ?? 'Item'

  function onAddToCart() {
    if (!selected?.id) {
      return
    }
    addToCart.mutate(
      { productVariantId: selected.id, quantity },
      {
        onSuccess: () =>
          toast({ title: 'Added to cart', description: `${productName} × ${quantity}` }),
        onError: (error) =>
          toast({
            variant: 'destructive',
            title: 'Could not add to cart',
            description: error instanceof Error ? error.message : 'Please try again.',
          }),
      },
    )
  }

  return (
    <div className="space-y-12">
      <div className="grid gap-8 md:grid-cols-2">
        {/* Keyed by the selected variant so the gallery resets to that variant's first image. */}
        <ProductGallery
          key={selected?.id ?? 'base'}
          images={product.images ?? []}
          variantId={selected?.id}
          fallbackBlobKey={product.primaryImageBlobKey}
          alt={product.name ?? 'Product'}
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
              <span className="text-2xl font-semibold">
                {formatCents(selected.priceCents ?? 0)}
              </span>
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

          {selected ? (
            <div className="flex items-center gap-3 pt-2">
              <Select
                aria-label="Quantity"
                className="w-20"
                value={quantity}
                disabled={!canAddToCart || addToCart.isPending}
                onChange={(event) => setQuantity(Number(event.target.value))}
              >
                {Array.from({ length: 10 }, (_, index) => index + 1).map((value) => (
                  <option key={value} value={value}>
                    {value}
                  </option>
                ))}
              </Select>
              <Button disabled={!canAddToCart || addToCart.isPending} onClick={onAddToCart}>
                {addToCart.isPending ? 'Adding…' : 'Add to cart'}
              </Button>
            </div>
          ) : null}

          {product.description ? (
            <p className="whitespace-pre-line text-sm text-muted-foreground">
              {product.description}
            </p>
          ) : null}
        </div>
      </div>

      {product.id ? <ProductReviews productId={product.id} /> : null}
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
