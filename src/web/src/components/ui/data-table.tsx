import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

export interface Column<T> {
  /** Stable key for the column (also used as the React key). */
  key: string
  header: ReactNode
  /** Renders the cell for a given row. */
  cell: (row: T) => ReactNode
  /** Optional extra classes on the header + cells (e.g. 'text-right'). */
  className?: string
}

interface DataTableProps<T> {
  columns: Column<T>[]
  rows: T[]
  getRowKey: (row: T) => string
  /** Rendered in place of the table when there are no rows. */
  empty?: ReactNode
}

/**
 * Thin, hand-built table primitive (PHASE_3_SCOPE.md §3.6 — "compose, not invent"). Columns describe
 * the headers + cell renderers; the PARENT owns data fetching, paging, and filtering. Deliberately
 * minimal (no built-in sort/virtualisation, no heavy dependency) so it stays a reusable building
 * block the admin pages compose rather than each hand-rolling a <table>.
 */
export function DataTable<T>({ columns, rows, getRowKey, empty }: DataTableProps<T>) {
  if (rows.length === 0 && empty !== undefined) {
    return <>{empty}</>
  }

  return (
    <div className="overflow-x-auto rounded-md border">
      <table className="w-full text-sm">
        <thead className="border-b bg-muted/50 text-left text-muted-foreground">
          <tr>
            {columns.map((col) => (
              <th key={col.key} className={cn('px-3 py-2 font-medium', col.className)}>
                {col.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={getRowKey(row)} className="border-b last:border-0">
              {columns.map((col) => (
                <td key={col.key} className={cn('px-3 py-2', col.className)}>
                  {col.cell(row)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
