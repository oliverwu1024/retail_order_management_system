import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { useSentimentSummaryQuery } from '../hooks/useSentimentSummaryQuery'
import { ProductsNeedingAttention } from './ProductsNeedingAttention'
import { SentimentMetricsTile } from './SentimentMetricsTile'

// Matches the server's "needing attention" threshold (avg < −0.2) so the panel derives from the
// one summary response rather than a second request.
const ATTENTION_THRESHOLD = -0.2

/** Admin dashboard sentiment card: metrics tile + Products-Needing-Attention panel (Story 4.3). */
export function SentimentSection() {
  const { data, isLoading, isError } = useSentimentSummaryQuery()
  const needsAttention = (data?.products ?? []).filter(
    (product) => (product.averageScore ?? 0) < ATTENTION_THRESHOLD,
  )

  return (
    <Card>
      <CardHeader>
        <CardTitle>Customer sentiment</CardTitle>
        <CardDescription>AI-scored review sentiment across the catalogue.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-5">
        {isLoading ? (
          <Skeleton className="h-16 w-full" />
        ) : isError || !data ? (
          <p className="text-sm text-destructive">Couldn’t load sentiment.</p>
        ) : (
          <>
            <SentimentMetricsTile summary={data} />
            <div className="space-y-2">
              <p className="text-sm font-medium">Products needing attention</p>
              <ProductsNeedingAttention products={needsAttention} />
            </div>
          </>
        )}
      </CardContent>
    </Card>
  )
}
