import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { EmptyState } from './empty-state'

describe('EmptyState', () => {
  it('renders the title', () => {
    render(<EmptyState title="No orders" />)
    expect(screen.getByText('No orders')).toBeInTheDocument()
  })

  it('renders the optional description and action', () => {
    render(
      <EmptyState
        title="No orders"
        description="Place one to get started."
        action={<button>Add</button>}
      />,
    )
    expect(screen.getByText('Place one to get started.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add' })).toBeInTheDocument()
  })

  it('omits the description when not provided', () => {
    render(<EmptyState title="Empty" />)
    expect(screen.queryByText('Place one to get started.')).not.toBeInTheDocument()
  })
})
