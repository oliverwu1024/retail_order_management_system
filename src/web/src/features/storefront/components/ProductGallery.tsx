import { useState } from 'react'
import { cn } from '@/lib/utils'
import type { ProductImage as ProductImageDto } from '@/lib/api/types'
import { ProductImage } from './ProductImage'

interface ProductGalleryProps {
  images: ProductImageDto[]
  /** The currently-selected variant; its images lead the gallery (general images follow). */
  variantId: string | undefined
  /** Single-image fallback (the primary blob key) when the product has no gallery rows. */
  fallbackBlobKey: string | null | undefined
  alt: string
}

/**
 * Storefront product gallery: a main image + a thumbnail strip. The gallery is variant-aware —
 * the selected variant's images lead, followed by the general (all-variant) images; the parent
 * keys this component by variant id, so picking a different variant re-renders to that variant's
 * first image. Falls back to the single primary image (or the placeholder) when there are no rows.
 */
export function ProductGallery({ images, variantId, fallbackBlobKey, alt }: ProductGalleryProps) {
  const variantImages = images
    .filter((i) => Boolean(variantId) && i.productVariantId === variantId)
    .sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))
  const generalImages = images
    .filter((i) => i.productVariantId == null)
    .sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))
  const gallery = [...variantImages, ...generalImages]

  const [active, setActive] = useState(0)
  const index = Math.min(active, Math.max(0, gallery.length - 1))

  if (gallery.length === 0) {
    return (
      <ProductImage
        blobKey={fallbackBlobKey}
        alt={alt}
        className="aspect-square w-full rounded-lg"
      />
    )
  }

  const current = gallery[index]

  return (
    <div className="space-y-3">
      <ProductImage
        blobKey={current.blobKey}
        alt={current.altText ?? alt}
        className="aspect-square w-full rounded-lg"
      />
      {gallery.length > 1 ? (
        <div className="flex flex-wrap gap-2">
          {gallery.map((image, i) => (
            <button
              key={image.id}
              type="button"
              onClick={() => setActive(i)}
              aria-label={`View image ${i + 1}`}
              aria-current={i === index}
              className={cn(
                'rounded-md border transition',
                i === index ? 'ring-2 ring-primary ring-offset-1' : 'opacity-70 hover:opacity-100',
              )}
            >
              <ProductImage
                blobKey={image.blobKey}
                alt={image.altText ?? alt}
                className="h-16 w-16 rounded-md"
              />
            </button>
          ))}
        </div>
      ) : null}
    </div>
  )
}
