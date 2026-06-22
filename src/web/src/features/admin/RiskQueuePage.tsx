import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { DataTable, type Column } from '@/components/ui/data-table'
import { EmptyState } from '@/components/ui/empty-state'
import { Pagination } from '@/components/ui/pagination'
import { Skeleton } from '@/components/ui/skeleton'
import type { Anomaly } from '@/lib/api/types'
import { useAcknowledgeAnomaly, useRiskQueueQuery } from './hooks/useRiskQueue'

const PAGE_SIZE = 20

/**
 * Order-anomaly Risk Queue: orders the anomaly scan flagged and that haven't been acknowledged yet.
 * Acknowledging one clears its Mark-Shipped block and removes it from the list. Staff + StoreManager
 * + Administrator (Anomaly.Manage).
 */
export function RiskQueuePage() {
  const [page, setPage] = useState(1)
  const { data, isLoading, isError } = useRiskQueueQuery({ page, pageSize: PAGE_SIZE })
  const acknowledge = useAcknowledgeAnomaly()

  const columns: Column<Anomaly>[] = [
    { key: 'order', header: 'Order', cell: (row) => `#${row.orderNumber ?? ''}` },
    { key: 'reason', header: 'Reason', cell: (row) => row.reason ?? '' },
    {
      key: 'score',
      header: 'Score',
      className: 'text-right',
      cell: (row) => formatScore(row.score),
    },
    { key: 'detected', header: 'Detected', cell: (row) => formatDateTime(row.detectedAt) },
    {
      key: 'ack',
      header: '',
      className: 'text-right',
      cell: (row) => (
        <Button
          variant="outline"
          size="sm"
          aria-label={`Acknowledge order #${row.orderNumber ?? ''}`}
          disabled={acknowledge.isPending || !row.id}
          onClick={() => row.id && acknowledge.mutate(row.id)}
        >
          Acknowledge
        </Button>
      ),
    },
  ]

  return (
    <section className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Risk queue</h1>
        <p className="text-sm text-muted-foreground">
          Orders flagged by the anomaly scan. A flagged order can’t be marked shipped until it’s
          acknowledged.
        </p>
      </div>

      {acknowledge.isError ? (
        <p role="alert" className="text-sm text-destructive">
          Couldn’t acknowledge that order. Please try again.
        </p>
      ) : null}

      {isError ? (
        <p className="text-sm text-destructive">Couldn’t load the risk queue. Please try again.</p>
      ) : isLoading ? (
        <div className="space-y-2">
          {Array.from({ length: 6 }).map((_, index) => (
            <Skeleton key={index} className="h-10 w-full" />
          ))}
        </div>
      ) : (
        <>
          <DataTable
            label="Risk queue"
            columns={columns}
            rows={data?.items ?? []}
            getRowKey={(row) => String(row.id)}
            empty={
              <EmptyState title="No flagged orders" description="Nothing needs review right now." />
            }
          />
          {data ? (
            <Pagination
              page={data.page ?? page}
              totalPages={data.totalPages ?? 1}
              hasPrevious={data.hasPrevious ?? false}
              hasNext={data.hasNext ?? false}
              onPageChange={setPage}
            />
          ) : null}
        </>
      )}
    </section>
  )
}

function formatScore(score: number | undefined): string {
  return score && score > 0 ? score.toFixed(1) : '—'
}

function formatDateTime(iso: string | undefined): string {
  return iso ? new Date(iso).toLocaleString() : ''
}
