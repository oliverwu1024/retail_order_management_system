import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { DataTable, type Column } from '@/components/ui/data-table'
import { Modal } from '@/components/ui/dialog'
import { EmptyState } from '@/components/ui/empty-state'
import { Pagination } from '@/components/ui/pagination'
import { Skeleton } from '@/components/ui/skeleton'
import type { ChatSession } from '@/lib/api/types'
import { useChatSessionQuery, useChatSessionsQuery } from './hooks/useChatSessions'

const PAGE_SIZE = 20

/** Support-chat diagnostics: list every conversation; each row opens a Modal with the full transcript
 *  (incl. tool calls). Read-only (Chat.View — StoreManager + Administrator). */
export function AdminChatPage() {
  const [page, setPage] = useState(1)
  const [selectedId, setSelectedId] = useState<string | null>(null)

  const { data, isLoading, isError } = useChatSessionsQuery(page, PAGE_SIZE)
  const detail = useChatSessionQuery(selectedId)

  const columns: Column<ChatSession>[] = [
    {
      key: 'lastMessageAt',
      header: 'Last activity',
      cell: (row) => formatDateTime(row.lastMessageAt),
    },
    { key: 'startedAt', header: 'Started', cell: (row) => formatDateTime(row.startedAt) },
    {
      key: 'customer',
      header: 'Customer',
      cell: (row) => (
        <span className="font-mono text-xs text-muted-foreground">
          {shortId(row.customerProfileId)}
        </span>
      ),
    },
    { key: 'messages', header: 'Messages', cell: (row) => row.messageCount },
    {
      key: 'view',
      header: '',
      className: 'text-right',
      cell: (row) => (
        <Button variant="ghost" size="sm" onClick={() => row.id && setSelectedId(row.id)}>
          View
        </Button>
      ),
    },
  ]

  return (
    <section className="space-y-6">
      <h1 className="text-2xl font-semibold tracking-tight">Chat sessions</h1>

      {isError ? (
        <p className="text-sm text-destructive">Couldn’t load chat sessions. Please try again.</p>
      ) : isLoading ? (
        <div className="space-y-2">
          {Array.from({ length: 8 }).map((_, index) => (
            <Skeleton key={index} className="h-10 w-full" />
          ))}
        </div>
      ) : (
        <>
          <DataTable
            label="Chat sessions"
            columns={columns}
            rows={data?.items ?? []}
            getRowKey={(row) => String(row.id)}
            empty={
              <EmptyState
                title="No chat sessions"
                description="No customer has used the support chat yet."
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
        open={selectedId !== null}
        onOpenChange={(open) => {
          if (!open) setSelectedId(null)
        }}
        title="Conversation"
        description={detail.data ? formatDateTime(detail.data.startedAt) : undefined}
      >
        {detail.isLoading ? (
          <Skeleton className="h-32 w-full" />
        ) : detail.isError ? (
          <p className="text-sm text-destructive">
            Couldn’t load this conversation. Please try again.
          </p>
        ) : detail.data ? (
          <div className="max-h-96 space-y-3 overflow-auto text-sm">
            {(detail.data.messages?.length ?? 0) === 0 ? (
              <p className="text-sm text-muted-foreground">No messages in this conversation.</p>
            ) : (
              detail.data.messages?.map((m, index) => (
                <div key={`${m.createdAt}-${index}`}>
                  <p className="text-xs font-medium text-muted-foreground">
                    {m.role}
                    {m.toolName ? ` · ${m.toolName}` : ''}
                  </p>
                  {m.toolPayloadJson ? (
                    <pre className="overflow-auto rounded bg-muted p-2 text-xs">
                      {prettyJson(m.toolPayloadJson)}
                    </pre>
                  ) : (
                    <p className="whitespace-pre-wrap">{m.content}</p>
                  )}
                </div>
              ))
            )}
          </div>
        ) : null}
      </Modal>
    </section>
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
  if (!id) return '—'
  return id.length > 12 ? `${id.slice(0, 8)}…` : id
}

function formatDateTime(iso: string | undefined): string {
  return iso ? new Date(iso).toLocaleString() : ''
}
