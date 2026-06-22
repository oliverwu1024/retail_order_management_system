import {
  Bar,
  CartesianGrid,
  ComposedChart,
  Legend,
  Line,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { Button } from '@/components/ui/button'
import { DataTable, type Column } from '@/components/ui/data-table'
import { EmptyState } from '@/components/ui/empty-state'
import { Skeleton } from '@/components/ui/skeleton'
import type { ReorderHint } from '@/lib/api/types'
import { useDismissReorderHint, useForecastQuery, useReorderHintsQuery } from './hooks/useForecast'

const PAGE_SIZE = 50

/**
 * Demand-forecast dashboard: a per-variant 14-day forecast (Holt-Winters) with its 80% prediction
 * band, and the resulting reorder hints with Dismiss. Staff + StoreManager + Administrator.
 */
export function ForecastPage() {
  const forecasts = useForecastQuery({ page: 1, pageSize: PAGE_SIZE })
  const reorder = useReorderHintsQuery({ page: 1, pageSize: PAGE_SIZE })
  const dismiss = useDismissReorderHint()

  const chartData = (forecasts.data?.items ?? []).map((f) => ({
    sku: f.sku ?? '',
    forecast: f.forecastedQty ?? 0,
    lower: f.lowerBound ?? 0,
    upper: f.upperBound ?? 0,
  }))

  const columns: Column<ReorderHint>[] = [
    { key: 'sku', header: 'Variant', cell: (row) => row.sku ?? '' },
    { key: 'product', header: 'Product', cell: (row) => row.productName ?? '' },
    {
      key: 'qty',
      header: 'Order qty',
      className: 'text-right',
      cell: (row) => String(row.recommendedOrderQty ?? 0),
    },
    { key: 'why', header: 'Reasoning', cell: (row) => row.reasoning ?? '' },
    {
      key: 'dismiss',
      header: '',
      className: 'text-right',
      cell: (row) => (
        <Button
          variant="outline"
          size="sm"
          aria-label={`Dismiss reorder hint for ${row.sku ?? ''}`}
          disabled={(dismiss.isPending && dismiss.variables === row.id) || !row.id}
          onClick={() => row.id && dismiss.mutate(row.id)}
        >
          Dismiss
        </Button>
      ),
    },
  ]

  return (
    <section className="space-y-8">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Demand forecast</h1>
        <p className="text-sm text-muted-foreground">
          Projected 14-day demand per variant, with a likely high–low range, and the reorder hints
          it drives.
        </p>
      </div>

      <div>
        <h2 className="mb-2 text-sm font-medium text-muted-foreground">
          14-day forecast by variant
        </h2>
        {forecasts.isError ? (
          <p className="text-sm text-destructive">Couldn’t load forecasts. Please try again.</p>
        ) : forecasts.isLoading ? (
          <Skeleton className="h-72 w-full" />
        ) : chartData.length === 0 ? (
          <EmptyState
            title="Forecast warming up"
            description="Forecasts appear once variants have enough sales history."
          />
        ) : (
          <div className="h-72 w-full">
            <p className="sr-only">
              Projected 14-day demand with a likely high–low range for {chartData.length} variant
              {chartData.length === 1 ? '' : 's'}; per-variant values are listed in the reorder
              hints table below.
            </p>
            <ResponsiveContainer width="100%" height="100%">
              <ComposedChart data={chartData} margin={{ top: 8, right: 16, bottom: 8, left: 8 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="#e5e7eb" />
                <XAxis dataKey="sku" tick={{ fontSize: 12 }} />
                <YAxis tick={{ fontSize: 12 }} width={48} />
                <Tooltip />
                <Legend />
                <Bar dataKey="forecast" name="Forecast" fill="#6366f1" />
                <Line
                  dataKey="upper"
                  name="Likely high"
                  stroke="#a3a3a3"
                  dot={false}
                  strokeDasharray="4 2"
                />
                <Line
                  dataKey="lower"
                  name="Likely low"
                  stroke="#a3a3a3"
                  dot={false}
                  strokeDasharray="4 2"
                />
              </ComposedChart>
            </ResponsiveContainer>
          </div>
        )}
      </div>

      <div className="space-y-3">
        <h2 className="text-sm font-medium text-muted-foreground">Reorder hints</h2>
        {dismiss.isError ? (
          <p role="alert" className="text-sm text-destructive">
            Couldn’t dismiss that hint. Please try again.
          </p>
        ) : null}
        {reorder.isError ? (
          <p className="text-sm text-destructive">Couldn’t load reorder hints. Please try again.</p>
        ) : reorder.isLoading ? (
          <div className="space-y-2">
            {Array.from({ length: 4 }).map((_, index) => (
              <Skeleton key={index} className="h-10 w-full" />
            ))}
          </div>
        ) : (
          <DataTable
            label="Reorder hints"
            columns={columns}
            rows={reorder.data?.items ?? []}
            getRowKey={(row) => String(row.id)}
            empty={
              <EmptyState
                title="No reorder hints"
                description="Nothing needs restocking right now."
              />
            }
          />
        )}
      </div>
    </section>
  )
}
