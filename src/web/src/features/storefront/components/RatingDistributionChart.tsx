import { RatingStars } from '@/components/ui/rating-stars'
import type { ReviewSummary } from '@/lib/api/types'

/**
 * Average rating + per-star distribution bars (REQUIREMENTS §6.2). A 5-row breakdown is clearer
 * and lighter as plain proportional bars than a charting library; each row is keyboard/SR-readable.
 */
export function RatingDistributionChart({ summary }: { summary: ReviewSummary }) {
  const average = summary.average ?? 0
  const count = summary.count ?? 0
  const distribution = summary.distribution ?? []
  const max = Math.max(1, ...distribution) // avoid divide-by-zero on an all-empty product

  if (count === 0) {
    return <p className="text-sm text-muted-foreground">No reviews yet — be the first to review this product.</p>
  }

  return (
    <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:gap-8">
      <div className="flex flex-col items-center gap-1">
        <span className="text-4xl font-semibold tabular-nums">{average.toFixed(1)}</span>
        <RatingStars value={average} />
        <span className="text-xs text-muted-foreground">
          {count} {count === 1 ? 'review' : 'reviews'}
        </span>
      </div>

      <ul className="flex-1 space-y-1.5">
        {[5, 4, 3, 2, 1].map((star) => {
          const starCount = distribution[star - 1] ?? 0
          const pct = Math.round((starCount / max) * 100)
          return (
            <li key={star} className="flex items-center gap-2 text-sm">
              <span className="w-3 text-right tabular-nums text-muted-foreground">{star}</span>
              <span aria-hidden="true" className="text-amber-400">
                ★
              </span>
              <span className="h-2 flex-1 overflow-hidden rounded-full bg-muted">
                <span
                  className="block h-full rounded-full bg-amber-400"
                  style={{ width: `${pct}%` }}
                />
              </span>
              <span className="w-6 text-right tabular-nums text-muted-foreground">{starCount}</span>
            </li>
          )
        })}
      </ul>
    </div>
  )
}
