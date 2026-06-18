import { useMutation } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { components } from '@/lib/api/schema'

type Schemas = components['schemas']

export type ProductCopy = Schemas['SuggestProductCopyResponse']
export interface CopyGenInput {
  tone: string
  length: string
}

/**
 * Generates AI product copy for a product. The result is NOT persisted by the API — the admin
 * reviews it and applies what they want into the product form, then saves. No cache invalidation.
 */
export function useCopyGenMutation(productId: string) {
  return useMutation({
    mutationFn: async (input: CopyGenInput): Promise<ProductCopy> => {
      const { data, error, response } = await apiClient.POST(
        '/api/v1/catalog/products/{id}/generate-copy',
        {
          params: { path: { id: productId } },
          body: { tone: input.tone, length: input.length },
        },
      )
      if (error || !data?.data) {
        const message =
          response?.status === 503
            ? 'The AI service is unavailable right now. Please try again shortly.'
            : 'Could not generate copy. Please try again.'
        throw new Error(message)
      }
      return data.data
    },
  })
}
