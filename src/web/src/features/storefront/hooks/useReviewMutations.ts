import { useMutation, useQueryClient } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { Review } from '@/lib/api/types'
import { reviewKeys } from './useReviewsQuery'

export interface SubmitReviewInput {
  rating: number
  body: string
}

/** Error carrying the HTTP status so callers can branch (401 login / 422 not-purchased / 409 duplicate). */
export class ReviewSubmitError extends Error {
  readonly status: number | undefined

  constructor(message: string, status: number | undefined) {
    super(message)
    this.name = 'ReviewSubmitError'
    this.status = status
  }
}

/** Submits a review for a product, then invalidates that product's review list + aggregate. */
export function useSubmitReview(productId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (input: SubmitReviewInput): Promise<Review> => {
      const { data, error, response } = await apiClient.POST(
        '/api/v1/products/{productId}/reviews',
        {
          params: { path: { productId } },
          body: { rating: input.rating, body: input.body },
        },
      )
      if (error || !data?.data) {
        // The API returns a friendly message in the failure envelope (e.g. "You can only review a
        // product you have purchased."); surface it, falling back to a generic line.
        const serverMessage = (error as { message?: string } | undefined)?.message
        throw new ReviewSubmitError(
          serverMessage ?? 'Could not submit your review.',
          response?.status,
        )
      }
      return data.data
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: reviewKeys.product(productId) })
    },
  })
}
