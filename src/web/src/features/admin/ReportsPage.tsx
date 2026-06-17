import {
  CartesianGrid,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { EmptyState } from '@/components/ui/empty-state'
import { Skeleton } from '@/components/ui/skeleton'
import { formatCents } from '@/lib/format'
import { useSalesReportQuery } from './hooks/useSalesReport'

/** Sales-by-day report: a Recharts line chart of paid-order revenue per day, plus a category
 *  breakdown. Reports.View (Staff + StoreManager + Administrator). */
export function ReportsPage() {
  const { data, isLoading, isError } = useSalesReportQuery()

  if (isLoading) {
    return <Skeleton className="h-72 w-full" />
  }
  if (isError || !data) {
    return <p className="text-sm text-destructive">Couldn’t load the report. Please try again.</p>
  }

  // Plot dollars (cents / 100); the data carries integer cents.
  const days = (data.days ?? []).map((d) => ({
    date: d.date,
    total: (d.totalSalesCents ?? 0) / 100,
  }))
  const categories = data.categories ?? []

  return (
    <section className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Sales report</h1>
        <p className="text-sm text-muted-foreground">Paid orders by day (last 30 days).</p>
      </div>

      {days.length === 0 ? (
        <EmptyState
          title="No sales yet"
          description="Sales appear here once orders are placed. (Rich sample data lands with the Phase-5 synthetic-order seeder.)"
        />
      ) : (
        <>
          <div className="h-72 w-full rounded-md border p-4">
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={days} margin={{ top: 8, right: 16, bottom: 8, left: 8 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="#e5e7eb" />
                <XAxis dataKey="date" tick={{ fontSize: 12 }} />
                <YAxis tick={{ fontSize: 12 }} width={56} tickFormatter={(value) => `$${value}`} />
                <Tooltip formatter={(value) => `$${Number(value).toFixed(2)}`} />
                <Line
                  type="monotone"
                  dataKey="total"
                  name="Sales"
                  stroke="#6366f1"
                  strokeWidth={2}
                  dot={false}
                />
              </LineChart>
            </ResponsiveContainer>
          </div>

          <div className="rounded-md border p-4">
            <h2 className="mb-3 text-sm font-semibold">By category</h2>
            {categories.length === 0 ? (
              <p className="text-sm text-muted-foreground">No category data.</p>
            ) : (
              <ul className="space-y-1 text-sm">
                {categories.map((category, index) => (
                  <li key={index} className="flex justify-between">
                    <span className="text-muted-foreground">{category.category}</span>
                    <span className="tabular-nums">
                      {formatCents(category.totalSalesCents ?? 0)}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </>
      )}
    </section>
  )
}
