// Base URL of the storage account that serves product images. Dev default points at
// the local Azurite blob endpoint; production sets VITE_BLOB_BASE_URL at build time.
// (Cast import.meta.env to index it without augmenting the Vite env typings.)
const BLOB_BASE =
  (import.meta.env as Record<string, string | undefined>).VITE_BLOB_BASE_URL ??
  'http://127.0.0.1:10000/devstoreaccount1'

const PRODUCT_IMAGES_CONTAINER = 'product-images'

/**
 * Resolves a product image blob key (e.g. "products/{id}/{guid}.png") to a full URL,
 * or null when the product has no image (callers render a placeholder).
 */
export function productImageUrl(blobKey: string | null | undefined): string | null {
  if (!blobKey) {
    return null
  }

  return `${BLOB_BASE}/${PRODUCT_IMAGES_CONTAINER}/${blobKey}`
}
