import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { Sheet } from './sheet'

describe('Sheet', () => {
  it('renders as a dialog labelled by its title, with its content', () => {
    render(
      <Sheet open onOpenChange={vi.fn()} title="Support">
        <p>Body content</p>
      </Sheet>,
    )
    expect(screen.getByRole('dialog', { name: 'Support' })).toBeInTheDocument()
    expect(screen.getByText('Body content')).toBeInTheDocument()
  })

  it('renders nothing when closed', () => {
    render(
      <Sheet open={false} onOpenChange={vi.fn()} title="Support">
        <p>Body content</p>
      </Sheet>,
    )
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('requests close on Escape', async () => {
    const onOpenChange = vi.fn()
    render(
      <Sheet open onOpenChange={onOpenChange} title="Support">
        <p>Body</p>
      </Sheet>,
    )
    await userEvent.keyboard('{Escape}')
    expect(onOpenChange).toHaveBeenCalledWith(false)
  })

  it('requests close via the Close button', async () => {
    const onOpenChange = vi.fn()
    render(
      <Sheet open onOpenChange={onOpenChange} title="Support">
        <p>Body</p>
      </Sheet>,
    )
    await userEvent.click(screen.getByRole('button', { name: /close/i }))
    expect(onOpenChange).toHaveBeenCalledWith(false)
  })
})
