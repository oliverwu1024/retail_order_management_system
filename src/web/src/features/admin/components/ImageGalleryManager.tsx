import { useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { toast } from '@/hooks/use-toast'
import { ProductImage } from '@/features/storefront/components/ProductImage'
import type { ProductImage as ProductImageDto, ProductVariant } from '@/lib/api/types'
import {
  useAddProductImage,
  useDeleteProductImage,
  useReorderProductImages,
  useUpdateProductImage,
} from '../hooks/useProductMutations'

// Client-side guards mirroring the backend (ImageFormat.cs: jpg/png/webp, ≤5 MB). The server
// re-checks via a magic-byte sniff — this just fails fast without a wasted round-trip.
const ALLOWED_TYPES = ['image/jpeg', 'image/png', 'image/webp']
const MAX_BYTES = 5 * 1024 * 1024

const GENERAL = '' // select value for "general (all variants)"

function variantLabel(variant: ProductVariant): string {
  const opts = Object.entries(variant.options ?? {})
    .map(([key, value]) => `${key}: ${value}`)
    .join(', ')
  return opts.length > 0 ? opts : (variant.sku ?? 'variant')
}

interface ImageGalleryManagerProps {
  productId: string
  images: ProductImageDto[]
  variants: ProductVariant[]
}

/**
 * Admin gallery manager: upload images (optionally scoped to a variant, with alt text), reorder
 * them, choose the primary (hero) image, edit alt text / variant association, and delete. Every
 * action calls the catalog image API and the product detail cache is invalidated so the gallery
 * reflects the server truth.
 */
export function ImageGalleryManager({ productId, images, variants }: ImageGalleryManagerProps) {
  const ordered = [...images].sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))
  const orderedIds = ordered.map((i) => i.id ?? '')

  // The mutations are owned HERE, not per-row, so a single in-flight write disables the WHOLE
  // gallery — otherwise a sibling row (or the upload box) could fire a conflicting write while
  // another mutation is mid-flight, churning the cache and racing the one-primary invariant.
  const addImage = useAddProductImage()
  const updateImage = useUpdateProductImage()
  const reorderImages = useReorderProductImages()
  const deleteImage = useDeleteProductImage()
  const galleryBusy =
    addImage.isPending || updateImage.isPending || reorderImages.isPending || deleteImage.isPending

  return (
    <section className="space-y-4">
      <div>
        <h2 className="text-lg font-semibold">Images</h2>
        <p className="text-xs text-muted-foreground">
          The primary image is the hero shown on cards and the cart. Variant-scoped images appear
          when that variant is selected on the storefront.
        </p>
      </div>

      <UploadRow
        productId={productId}
        variants={variants}
        addImage={addImage}
        galleryBusy={galleryBusy}
      />

      {ordered.length === 0 ? (
        <p className="text-sm text-muted-foreground">No images yet — upload one above.</p>
      ) : (
        <ul className="space-y-3">
          {ordered.map((image, index) => (
            <GalleryRow
              key={image.id}
              productId={productId}
              image={image}
              variants={variants}
              index={index}
              orderedIds={orderedIds}
              updateImage={updateImage}
              reorderImages={reorderImages}
              deleteImage={deleteImage}
              galleryBusy={galleryBusy}
            />
          ))}
        </ul>
      )}
    </section>
  )
}

function UploadRow({
  productId,
  variants,
  addImage,
  galleryBusy,
}: {
  productId: string
  variants: ProductVariant[]
  addImage: ReturnType<typeof useAddProductImage>
  galleryBusy: boolean
}) {
  const inputRef = useRef<HTMLInputElement>(null)
  const [file, setFile] = useState<File | null>(null)
  const [variantId, setVariantId] = useState<string>(GENERAL)
  const [altText, setAltText] = useState('')

  function onPick(event: React.ChangeEvent<HTMLInputElement>) {
    const picked = event.target.files?.[0] ?? null
    if (picked && !ALLOWED_TYPES.includes(picked.type)) {
      toast({
        variant: 'destructive',
        title: 'Unsupported file',
        description: 'Use JPEG, PNG, or WebP.',
      })
      setFile(null)
      return
    }
    if (picked && picked.size > MAX_BYTES) {
      toast({
        variant: 'destructive',
        title: 'File too large',
        description: 'Images must be 5 MB or smaller.',
      })
      setFile(null)
      return
    }
    setFile(picked)
  }

  function onUpload() {
    if (!file) {
      return
    }
    addImage.mutate(
      { id: productId, file, variantId: variantId || null, altText: altText || null },
      {
        onSuccess: () => {
          setFile(null)
          setAltText('')
          setVariantId(GENERAL)
          if (inputRef.current) {
            inputRef.current.value = ''
          }
          toast({ title: 'Image added' })
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
    <div className="space-y-3 rounded-md border border-dashed p-4">
      <input
        ref={inputRef}
        type="file"
        accept="image/jpeg,image/png,image/webp"
        onChange={onPick}
        className="text-sm file:mr-3 file:rounded-md file:border file:border-input file:bg-background file:px-3 file:py-1.5 file:text-sm file:font-medium"
      />
      <div className="flex flex-wrap items-end gap-3">
        <label className="space-y-1 text-sm">
          <span className="block text-muted-foreground">For variant</span>
          <VariantSelect
            value={variantId}
            variants={variants}
            onChange={setVariantId}
            disabled={galleryBusy}
          />
        </label>
        <label className="space-y-1 text-sm">
          <span className="block text-muted-foreground">Alt text</span>
          <Input
            value={altText}
            maxLength={200}
            onChange={(e) => setAltText(e.target.value)}
            placeholder="e.g. front view"
            className="w-56"
            disabled={galleryBusy}
          />
        </label>
        <Button type="button" size="sm" disabled={!file || galleryBusy} onClick={onUpload}>
          {addImage.isPending ? 'Uploading…' : 'Add image'}
        </Button>
      </div>
      <p className="text-xs text-muted-foreground">JPEG, PNG, or WebP · up to 5 MB.</p>
    </div>
  )
}

function GalleryRow({
  productId,
  image,
  variants,
  index,
  orderedIds,
  updateImage,
  reorderImages,
  deleteImage,
  galleryBusy,
}: {
  productId: string
  image: ProductImageDto
  variants: ProductVariant[]
  index: number
  orderedIds: string[]
  updateImage: ReturnType<typeof useUpdateProductImage>
  reorderImages: ReturnType<typeof useReorderProductImages>
  deleteImage: ReturnType<typeof useDeleteProductImage>
  galleryBusy: boolean
}) {
  const imageId = image.id ?? ''
  // Gallery-wide: any in-flight mutation disables every row's controls, not just the acting one.
  const busy = galleryBusy

  // Controlled local state for the editable fields so a save uses the LIVE values rather than the
  // (possibly stale) image prop — this prevents lost updates when promoting to primary or changing
  // the variant right after typing alt text (the API replaces altText + variant on every PUT).
  const [altText, setAltText] = useState(image.altText ?? '')
  const [variantId, setVariantId] = useState(image.productVariantId ?? GENERAL)

  // Sends the current local state; `overrides` lets the just-changed field pass its new value
  // before React state settles.
  function save(
    overrides: { altText?: string | null; variantId?: string | null; isPrimary?: boolean } = {},
  ) {
    updateImage.mutate(
      {
        id: productId,
        imageId,
        body: {
          altText: overrides.altText !== undefined ? overrides.altText : altText.trim() || null,
          productVariantId:
            overrides.variantId !== undefined ? overrides.variantId : variantId || null,
          isPrimary: overrides.isPrimary,
        },
      },
      { onError: () => toast({ variant: 'destructive', title: 'Update failed' }) },
    )
  }

  function move(direction: -1 | 1) {
    const target = index + direction
    if (target < 0 || target >= orderedIds.length) {
      return
    }
    const next = [...orderedIds]
    ;[next[index], next[target]] = [next[target], next[index]]
    reorderImages.mutate(
      { id: productId, imageIds: next },
      { onError: () => toast({ variant: 'destructive', title: 'Reorder failed' }) },
    )
  }

  function remove() {
    if (!window.confirm('Delete this image?')) {
      return
    }
    deleteImage.mutate(
      { id: productId, imageId },
      {
        onSuccess: () => toast({ title: 'Image deleted' }),
        onError: () => toast({ variant: 'destructive', title: 'Delete failed' }),
      },
    )
  }

  return (
    <li className="flex items-start gap-4 rounded-md border p-3">
      <ProductImage
        blobKey={image.blobKey}
        alt={image.altText ?? 'Product image'}
        className="h-24 w-24 rounded-md border"
      />

      <div className="flex-1 space-y-2">
        <div className="flex items-center gap-2">
          {image.isPrimary ? (
            <span className="rounded-full bg-primary px-2 py-0.5 text-xs font-medium text-primary-foreground">
              Primary
            </span>
          ) : (
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={busy}
              onClick={() => save({ isPrimary: true })}
            >
              Set primary
            </Button>
          )}
          <span className="text-xs text-muted-foreground">Position {index + 1}</span>
        </div>

        <Input
          value={altText}
          maxLength={200}
          placeholder="Alt text"
          className="w-full max-w-md"
          disabled={busy}
          onChange={(e) => setAltText(e.target.value)}
          onBlur={() => {
            if ((altText.trim() || null) !== (image.altText ?? null)) {
              save()
            }
          }}
        />

        <VariantSelect
          value={variantId}
          variants={variants}
          disabled={busy}
          onChange={(value) => {
            setVariantId(value)
            save({ variantId: value || null })
          }}
        />
      </div>

      <div className="flex flex-col items-end gap-1">
        <div className="flex gap-1">
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={busy || index === 0}
            onClick={() => move(-1)}
            aria-label="Move up"
          >
            ↑
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={busy || index === orderedIds.length - 1}
            onClick={() => move(1)}
            aria-label="Move down"
          >
            ↓
          </Button>
        </div>
        <Button type="button" variant="outline" size="sm" disabled={busy} onClick={remove}>
          Delete
        </Button>
      </div>
    </li>
  )
}

function VariantSelect({
  value,
  variants,
  onChange,
  disabled,
}: {
  value: string
  variants: ProductVariant[]
  onChange: (value: string) => void
  disabled?: boolean
}) {
  return (
    <select
      value={value}
      disabled={disabled}
      onChange={(e) => onChange(e.target.value)}
      className="h-9 rounded-md border border-input bg-background px-2 text-sm"
    >
      <option value={GENERAL}>General (all variants)</option>
      {variants.map((variant) => (
        <option key={variant.id} value={variant.id ?? ''}>
          {variantLabel(variant)}
        </option>
      ))}
    </select>
  )
}
