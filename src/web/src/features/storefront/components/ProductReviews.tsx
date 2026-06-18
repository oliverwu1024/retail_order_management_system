import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Pagination } from '@/components/ui/pagination'
import { Skeleton } from '@/components/ui/skeleton'
import { useAuthStore } from '@/lib/store/auth-store'
import { useReviewsQuery } from '../hooks/useReviewsQuery'
import { RatingDistributionChart } from './RatingDistributionChart'
import { ReviewsList } from './ReviewsList'
import { ReviewSubmitForm } from './ReviewSubmitForm'

/** The product-page reviews section: aggregate + (purchaser) submit form + paginated list. */
export function ProductReviews({ productId }: { productId: string }) {
  const [page, setPage] = useState(1)
  const { data, isLoading, isError } = useReviewsQuery(productId, page)
  const user = useAuthStore((state) => state.user)
  const isCustomer = user?.roles?.includes('Customer') ?? false

  return (
    <section aria-labelledby="reviews-heading" className="space-y-6 border-t pt-8">
      <h2 id="reviews-heading" className="text-2xl font-semibold">
        Customer reviews
      </h2>

      {isLoading ? (
        <div className="space-y-3">
          <Skeleton className="h-20 w-full" />
          <Skeleton className="h-16 w-full" />
        </div>
      ) : isError || !data ? (
        <p className="text-sm text-destructive">Couldn’t load reviews. Please try again later.</p>
      ) : (
        <>
          {data.summary ? <RatingDistributionChart summary={data.summary} /> : null}

          {isCustomer ? (
            <ReviewSubmitForm productId={productId} />
          ) : (
            <p className="text-sm text-muted-foreground">
              <Link to="/login" className="font-medium underline">
                Log in
              </Link>{' '}
              to write a review. You can review products you’ve purchased.
            </p>
          )}

          {(data.page?.items?.length ?? 0) === 0 ? (
            <p className="text-sm text-muted-foreground">No reviews yet.</p>
          ) : (
            <>
              <ReviewsList reviews={data.page?.items ?? []} />
              <Pagination
                page={data.page?.page ?? 1}
                totalPages={data.page?.totalPages ?? 1}
                hasPrevious={data.page?.hasPrevious ?? false}
                hasNext={data.page?.hasNext ?? false}
                onPageChange={setPage}
              />
            </>
          )}
        </>
      )}
    </section>
  )
}
