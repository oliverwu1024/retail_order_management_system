import { useMutation, useQueryClient } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { components } from '@/lib/api/schema'
import type { ProductDetail } from '@/lib/api/types'
import { adminProductKeys } from './useAdminProductsQuery'

type Schemas = components['schemas']

// ─────────────────────────────────────────────────────────────────────────────
//  Admin product mutations (create / update / soft-delete / image upload).
//
//  Each mutation invalidates the caches its change can affect, so the UI
//  refetches the truth instead of guessing it:
//    - adminProductKeys.all  → the admin table (drafts included)
//    - ['products']          → the public storefront grid (publish/unpublish,
//                              price, image all show there)
//  The CSRF header is attached automatically by the client middleware
//  (lib/api/client.ts) on these POST/PUT/DELETE calls.
// ─────────────────────────────────────────────────────────────────────────────

/** Invalidate both the admin table and the public storefront after a write. */
function useInvalidateProducts() {
  const queryClient = useQueryClient()
  return () => {
    void queryClient.invalidateQueries({ queryKey: adminProductKeys.all })
    void queryClient.invalidateQueries({ queryKey: ['products'] })
  }
}

/** Creates a product; resolves to the new ProductDetailDto so the caller can route to its edit page. */
export function useCreateProduct() {
  const invalidate = useInvalidateProducts()
  return useMutation({
    mutationFn: async (body: Schemas['CreateProductRequest']): Promise<ProductDetail> => {
      const { data, error } = await apiClient.POST('/api/v1/catalog/products', { body })
      if (error || !data?.data) {
        throw new Error('Failed to create the product.')
      }
      return data.data
    },
    onSuccess: invalidate,
  })
}

/** Updates a product's editable fields (not its SKU). */
export function useUpdateProduct() {
  const queryClient = useQueryClient()
  const invalidate = useInvalidateProducts()
  return useMutation({
    mutationFn: async (vars: {
      id: string
      body: Schemas['UpdateProductRequest']
    }): Promise<ProductDetail> => {
      const { data, error } = await apiClient.PUT('/api/v1/catalog/products/{id}', {
        params: { path: { id: vars.id } },
        body: vars.body,
      })
      if (error || !data?.data) {
        throw new Error('Failed to save the product.')
      }
      return data.data
    },
    onSuccess: (_data, vars) => {
      invalidate()
      void queryClient.invalidateQueries({ queryKey: adminProductKeys.detail(vars.id) })
    },
  })
}

/** Soft-deletes a product (recoverable; disappears from the storefront). */
export function useDeleteProduct() {
  const invalidate = useInvalidateProducts()
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error } = await apiClient.DELETE('/api/v1/catalog/products/{id}', {
        params: { path: { id } },
      })
      if (error) {
        throw new Error('Failed to delete the product.')
      }
    },
    onSuccess: invalidate,
  })
}

// After any gallery change, refresh the public/admin grids, this product's admin detail (the edit
// page reads images[] from it) AND the public storefront detail (keyed by slug) so a buyer viewing
// the product sees the new images without a manual refresh.
function useInvalidateGallery() {
  const queryClient = useQueryClient()
  const invalidate = useInvalidateProducts()
  return (productId: string, slug?: string | null) => {
    invalidate()
    void queryClient.invalidateQueries({ queryKey: adminProductKeys.detail(productId) })
    if (slug) {
      void queryClient.invalidateQueries({ queryKey: ['product', slug] })
    }
  }
}

/**
 * Adds an image to a product's gallery (optionally scoped to a variant, with alt text).
 *
 * WHY THE bodySerializer DANCE?
 * openapi-fetch JSON-serializes bodies by default. For multipart/form-data we override
 * bodySerializer to build a FormData and return it — so the browser sets the multipart boundary
 * Content-Type itself. The typed `body` only satisfies openapi-fetch's "has a body" check (it
 * skips the body entirely when undefined); bodySerializer is what actually runs.
 */
export function useAddProductImage() {
  const invalidateGallery = useInvalidateGallery()
  return useMutation({
    mutationFn: async (vars: {
      id: string
      file: File
      variantId?: string | null
      altText?: string | null
    }): Promise<ProductDetail> => {
      const { data, error } = await apiClient.POST('/api/v1/catalog/products/{id}/images', {
        params: { path: { id: vars.id } },
        body: { file: vars.file as unknown as string },
        bodySerializer: () => {
          const formData = new FormData()
          formData.append('file', vars.file)
          if (vars.variantId) {
            formData.append('variantId', vars.variantId)
          }
          if (vars.altText) {
            formData.append('altText', vars.altText)
          }
          return formData
        },
      })
      if (error || !data?.data) {
        throw new Error('Failed to upload the image.')
      }
      return data.data
    },
    onSuccess: (data, vars) => invalidateGallery(vars.id, data.slug),
  })
}

/** Edits a gallery image (alt text, variant association, promote-to-primary). */
export function useUpdateProductImage() {
  const invalidateGallery = useInvalidateGallery()
  return useMutation({
    mutationFn: async (vars: {
      id: string
      imageId: string
      body: Schemas['UpdateProductImageRequest']
    }): Promise<ProductDetail> => {
      const { data, error } = await apiClient.PUT(
        '/api/v1/catalog/products/{id}/images/{imageId}',
        {
          params: { path: { id: vars.id, imageId: vars.imageId } },
          body: vars.body,
        },
      )
      if (error || !data?.data) {
        throw new Error('Failed to update the image.')
      }
      return data.data
    },
    onSuccess: (data, vars) => invalidateGallery(vars.id, data.slug),
  })
}

/** Reorders a product's gallery (the full set of image ids in display order). */
export function useReorderProductImages() {
  const invalidateGallery = useInvalidateGallery()
  return useMutation({
    mutationFn: async (vars: { id: string; imageIds: string[] }): Promise<ProductDetail> => {
      const { data, error } = await apiClient.PUT('/api/v1/catalog/products/{id}/images/order', {
        params: { path: { id: vars.id } },
        body: { imageIds: vars.imageIds },
      })
      if (error || !data?.data) {
        throw new Error('Failed to reorder the images.')
      }
      return data.data
    },
    onSuccess: (data, vars) => invalidateGallery(vars.id, data.slug),
  })
}

/** Deletes a gallery image (the next image is promoted to primary if needed). */
export function useDeleteProductImage() {
  const invalidateGallery = useInvalidateGallery()
  return useMutation({
    mutationFn: async (vars: { id: string; imageId: string }): Promise<ProductDetail> => {
      const { data, error } = await apiClient.DELETE(
        '/api/v1/catalog/products/{id}/images/{imageId}',
        {
          params: { path: { id: vars.id, imageId: vars.imageId } },
        },
      )
      if (error || !data?.data) {
        throw new Error('Failed to delete the image.')
      }
      return data.data
    },
    onSuccess: (data, vars) => invalidateGallery(vars.id, data.slug),
  })
}
