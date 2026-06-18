import { RatingStars } from '@/components/ui/rating-stars'
import type { Review } from '@/lib/api/types'

/** The reviews on the current page. Empty/loading states are handled by the parent. */
export function ReviewsList({ reviews }: { reviews: Review[] }) {
  return (
    <ul className="divide-y">
      {reviews.map((review) => (
        <li key={review.id} className="space-y-1.5 py-4">
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
            <RatingStars value={review.rating ?? 0} size="sm" />
            <span className="text-sm font-medium">{review.customerName}</span>
            {review.createdAt ? (
              <span className="text-xs text-muted-foreground">
                {new Date(review.createdAt).toLocaleDateString(undefined, {
                  year: 'numeric',
                  month: 'short',
                  day: 'numeric',
                })}
              </span>
            ) : null}
          </div>
          <p className="whitespace-pre-line text-sm text-muted-foreground">{review.body}</p>
        </li>
      ))}
    </ul>
  )
}
