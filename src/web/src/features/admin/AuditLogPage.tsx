import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { DataTable, type Column } from '@/components/ui/data-table'
import { Modal } from '@/components/ui/dialog'
import { EmptyState } from '@/components/ui/empty-state'
import { Input } from '@/components/ui/input'
import { Pagination } from '@/components/ui/pagination'
import { Skeleton } from '@/components/ui/skeleton'
import type { AuditLog } from '@/lib/api/types'
import { useAuditLogsQuery } from './hooks/useAuditLogs'

const PAGE_SIZE = 20

/** Audit-log viewer: search the immutable trail by entity type / actor; each row opens a Modal with
 *  the before/after JSON. View-only (Staff + StoreManager + Administrator). */
export function AuditLogPage() {
  const [page, setPage] = useState(1)
  const [entityType, setEntityType] = useState('')
  const [actor, setActor] = useState('')
  const [detail, setDetail] = useState<AuditLog | null>(null)

  const { data, isLoading, isError } = useAuditLogsQuery({
    page,
    pageSize: PAGE_SIZE,
    entityType: entityType || undefined,
    actor: actor || undefined,
  })

  function setFilter(setter: (value: string) => void, value: string) {
    setter(value)
    setPage(1)
  }

  const columns: Column<AuditLog>[] = [
    { key: 'when', header: 'When', cell: (row) => formatDateTime(row.occurredAt) },
    { key: 'actor', header: 'Actor', cell: (row) => row.actor },
    { key: 'action', header: 'Action', cell: (row) => row.action },
    {
      key: 'entity',
      header: 'Entity',
      cell: (row) => (
        <span>
          {row.entityType}{' '}
          <span className="font-mono text-xs text-muted-foreground">{shortId(row.entityId)}</span>
        </span>
      ),
    },
    {
      key: 'details',
      header: '',
      className: 'text-right',
      cell: (row) => (
        <Button variant="ghost" size="sm" onClick={() => setDetail(row)}>
          Details
        </Button>
      ),
    },
  ]

  return (
    <section className="space-y-6">
      <h1 className="text-2xl font-semibold tracking-tight">Audit log</h1>

      <div className="flex flex-wrap gap-3">
        <Input
          placeholder="Entity type (e.g. Order)"
          value={entityType}
          onChange={(e) => setFilter(setEntityType, e.target.value)}
          className="max-w-56"
        />
        <Input
          placeholder="Actor (user id or system)"
          value={actor}
          onChange={(e) => setFilter(setActor, e.target.value)}
          className="max-w-72"
        />
      </div>

      {isError ? (
        <p className="text-sm text-destructive">Couldn’t load the audit log. Please try again.</p>
      ) : isLoading ? (
        <div className="space-y-2">
          {Array.from({ length: 8 }).map((_, index) => (
            <Skeleton key={index} className="h-10 w-full" />
          ))}
        </div>
      ) : (
        <>
          <DataTable
            label="Audit log"
            columns={columns}
            rows={data?.items ?? []}
            getRowKey={(row) => String(row.id)}
            empty={
              <EmptyState
                title="No audit entries"
                description="No entries match the current filters."
              />
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

      <Modal
        open={detail !== null}
        onOpenChange={(open) => {
          if (!open) setDetail(null)
        }}
        title={detail ? `${detail.action} · ${detail.entityType}` : ''}
        description={detail ? `${detail.actor} · ${formatDateTime(detail.occurredAt)}` : undefined}
      >
        {detail ? (
          <div className="space-y-3 text-xs">
            <JsonBlock label="Before" json={detail.beforeJson} />
            <JsonBlock label="After" json={detail.afterJson} />
          </div>
        ) : null}
      </Modal>
    </section>
  )
}

function JsonBlock({ label, json }: { label: string; json: string | null | undefined }) {
  return (
    <div>
      <p className="mb-1 font-medium text-muted-foreground">{label}</p>
      <pre className="max-h-48 overflow-auto rounded bg-muted p-2">
        {json ? prettyJson(json) : '—'}
      </pre>
    </div>
  )
}

function prettyJson(value: string): string {
  try {
    return JSON.stringify(JSON.parse(value), null, 2)
  } catch {
    return value
  }
}

function shortId(id: string | null | undefined): string {
  if (!id) return ''
  return id.length > 12 ? `${id.slice(0, 8)}…` : id
}

function formatDateTime(iso: string | undefined): string {
  return iso ? new Date(iso).toLocaleString() : ''
}
