import type { SentimentSummary } from '@/lib/api/types'

// Color-coded chips for the four Azure AI Language labels — clearer than a 4-bar chart for
// categorical counts (same call as the storefront rating distribution).
const LABEL_STYLES: Record<string, string> = {
  Positive: 'bg-emerald-100 text-emerald-800',
  Neutral: 'bg-muted text-muted-foreground',
  Negative: 'bg-red-100 text-red-800',
  Mixed: 'bg-amber-100 text-amber-800',
}

/** Overall sentiment average + scored count + the label distribution. */
export function SentimentMetricsTile({ summary }: { summary: SentimentSummary }) {
  const average = summary.averageScore
  const scored = summary.scoredReviews ?? 0
  const labels = summary.labelDistribution ?? []

  return (
    <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:gap-8">
      <div>
        <p className="text-3xl font-semibold tabular-nums">
          {average != null ? average.toFixed(2) : '—'}
        </p>
        <p className="text-xs text-muted-foreground">avg sentiment · {scored} scored</p>
      </div>
      <div className="flex flex-wrap gap-2">
        {labels.length === 0 ? (
          <span className="text-sm text-muted-foreground">No reviews scored yet.</span>
        ) : (
          labels.map((entry) => (
            <span
              key={entry.label}
              className={`rounded-full px-2.5 py-1 text-xs font-medium ${LABEL_STYLES[entry.label ?? ''] ?? 'bg-muted'}`}
            >
              {entry.label}: {entry.count}
            </span>
          ))
        )}
      </div>
    </div>
  )
}
