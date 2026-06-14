import { useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import { toast } from '@/hooks/use-toast'
import { ProductImage } from '@/features/storefront/components/ProductImage'
import { useUploadProductImage } from '../hooks/useProductMutations'

interface ImageUploadFieldProps {
  productId: string
  currentBlobKey: string | null | undefined
}

// Client-side guards that mirror the backend's (ProductImage.cs: jpg/png/webp, ≤5 MB).
// The server re-checks — this is just to fail fast without a wasted round-trip.
const ALLOWED_TYPES = ['image/jpeg', 'image/png', 'image/webp']
const MAX_BYTES = 5 * 1024 * 1024

/**
 * Primary-image upload for the edit page: shows the current image (or the
 * "No image" placeholder) and lets the admin pick a replacement. The chosen
 * file is validated locally, then sent as multipart/form-data; on success the
 * product detail cache is invalidated so the new image appears immediately.
 */
export function ImageUploadField({ productId, currentBlobKey }: ImageUploadFieldProps) {
  const uploadImage = useUploadProductImage()
  const inputRef = useRef<HTMLInputElement>(null)
  const [selected, setSelected] = useState<File | null>(null)

  function onPick(event: React.ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0] ?? null
    if (file && !ALLOWED_TYPES.includes(file.type)) {
      toast({
        variant: 'destructive',
        title: 'Unsupported file',
        description: 'Use JPEG, PNG, or WebP.',
      })
      setSelected(null)
      return
    }
    if (file && file.size > MAX_BYTES) {
      toast({
        variant: 'destructive',
        title: 'File too large',
        description: 'Images must be 5 MB or smaller.',
      })
      setSelected(null)
      return
    }
    setSelected(file)
  }

  function onUpload() {
    if (!selected) {
      return
    }
    uploadImage.mutate(
      { id: productId, file: selected },
      {
        onSuccess: () => {
          setSelected(null)
          if (inputRef.current) {
            inputRef.current.value = ''
          }
          toast({ title: 'Image updated' })
        },
        onError: () =>
          toast({
            variant: 'destructive',
            title: 'Upload failed',
            description: 'Please try again.',
          }),
      },
    )
  }

  return (
    <section className="space-y-3">
      <h2 className="text-lg font-semibold">Primary image</h2>
      <div className="flex items-start gap-4">
        <ProductImage
          blobKey={currentBlobKey}
          alt="Product image"
          className="h-32 w-32 rounded-md border"
        />
        <div className="space-y-2">
          <input
            ref={inputRef}
            type="file"
            accept="image/jpeg,image/png,image/webp"
            onChange={onPick}
            className="text-sm file:mr-3 file:rounded-md file:border file:border-input file:bg-background file:px-3 file:py-1.5 file:text-sm file:font-medium"
          />
          <p className="text-xs text-muted-foreground">JPEG, PNG, or WebP · up to 5 MB.</p>
          <Button
            type="button"
            size="sm"
            disabled={!selected || uploadImage.isPending}
            onClick={onUpload}
          >
            {uploadImage.isPending ? 'Uploading…' : 'Upload image'}
          </Button>
        </div>
      </div>
    </section>
  )
}
