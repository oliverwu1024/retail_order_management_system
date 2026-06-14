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

/**
 * Uploads/replaces a product's primary image.
 *
 * WHY THE bodySerializer DANCE?
 * openapi-fetch JSON-serializes bodies by default. For multipart/form-data we
 * override bodySerializer to build a FormData and return it — that makes the
 * browser set the multipart boundary Content-Type itself. The `body` value is
 * required (openapi-fetch skips a request body entirely when it's undefined),
 * so we pass the File through a cast; bodySerializer is what actually runs.
 */
export function useUploadProductImage() {
  const queryClient = useQueryClient()
  const invalidate = useInvalidateProducts()
  return useMutation({
    mutationFn: async (vars: { id: string; file: File }): Promise<ProductDetail> => {
      const { data, error } = await apiClient.POST('/api/v1/catalog/products/{id}/image', {
        params: { path: { id: vars.id } },
        body: { file: vars.file as unknown as string },
        bodySerializer: () => {
          const formData = new FormData()
          formData.append('file', vars.file)
          return formData
        },
      })
      if (error || !data?.data) {
        throw new Error('Failed to upload the image.')
      }
      return data.data
    },
    onSuccess: (_data, vars) => {
      invalidate()
      void queryClient.invalidateQueries({ queryKey: adminProductKeys.detail(vars.id) })
    },
  })
}
