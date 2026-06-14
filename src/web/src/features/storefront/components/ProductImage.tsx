import { productImageUrl } from '@/lib/images'
import { cn } from '@/lib/utils'

interface ProductImageProps {
  blobKey: string | null | undefined
  alt: string
  className?: string
}

/** Renders a product image, or a neutral placeholder when there's no image yet. */
export function ProductImage({ blobKey, alt, className }: ProductImageProps) {
  const url = productImageUrl(blobKey)

  if (!url) {
    return (
      <div
        className={cn('flex items-center justify-center bg-muted text-muted-foreground', className)}
        role="img"
        aria-label={alt}
      >
        <span className="text-xs">No image</span>
      </div>
    )
  }

  return <img src={url} alt={alt} loading="lazy" className={cn('object-cover', className)} />
}
