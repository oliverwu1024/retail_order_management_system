import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { DataTable, type Column } from './data-table'

interface Row {
  id: string
  name: string
}

const columns: Column<Row>[] = [{ key: 'name', header: 'Name', cell: (row) => row.name }]

describe('DataTable', () => {
  it('renders the header and a row per item', () => {
    const rows: Row[] = [
      { id: '1', name: 'Alpha' },
      { id: '2', name: 'Beta' },
    ]
    render(<DataTable columns={columns} rows={rows} getRowKey={(row) => row.id} />)

    expect(screen.getByText('Name')).toBeInTheDocument()
    expect(screen.getByText('Alpha')).toBeInTheDocument()
    expect(screen.getByText('Beta')).toBeInTheDocument()
    // header row + 2 body rows
    expect(screen.getAllByRole('row')).toHaveLength(3)
  })

  it('renders the empty fallback (and no table) when there are no rows', () => {
    render(
      <DataTable
        columns={columns}
        rows={[]}
        getRowKey={(row) => row.id}
        empty={<p>Nothing here</p>}
      />,
    )

    expect(screen.getByText('Nothing here')).toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })
})
