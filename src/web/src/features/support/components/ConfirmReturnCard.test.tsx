import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import type { ChatProposedAction } from '@/lib/api/types'
import { ConfirmReturnCard } from './ConfirmReturnCard'

const action: ChatProposedAction = {
  type: 'confirm_return',
  orderId: 'order-1',
  orderNumber: 10012,
  refundAmountCents: 4200,
}

describe('ConfirmReturnCard', () => {
  it('shows the order number and a refund line', () => {
    render(
      <ConfirmReturnCard
        action={action}
        onConfirm={vi.fn()}
        onDismiss={vi.fn()}
        isConfirming={false}
      />,
    )
    expect(screen.getByText(/cancel order #10012/i)).toBeInTheDocument()
    expect(screen.getByText(/refunded/i)).toBeInTheDocument()
  })

  it('calls onConfirm when confirmed', async () => {
    const onConfirm = vi.fn()
    render(
      <ConfirmReturnCard
        action={action}
        onConfirm={onConfirm}
        onDismiss={vi.fn()}
        isConfirming={false}
      />,
    )
    await userEvent.click(screen.getByRole('button', { name: /confirm refund/i }))
    expect(onConfirm).toHaveBeenCalled()
  })

  it('calls onDismiss when kept', async () => {
    const onDismiss = vi.fn()
    render(
      <ConfirmReturnCard
        action={action}
        onConfirm={vi.fn()}
        onDismiss={onDismiss}
        isConfirming={false}
      />,
    )
    await userEvent.click(screen.getByRole('button', { name: /keep order/i }))
    expect(onDismiss).toHaveBeenCalled()
  })

  it('disables both buttons while the cancel is in flight', () => {
    render(
      <ConfirmReturnCard action={action} onConfirm={vi.fn()} onDismiss={vi.fn()} isConfirming />,
    )
    expect(screen.getByRole('button', { name: /cancelling/i })).toBeDisabled()
    expect(screen.getByRole('button', { name: /keep order/i })).toBeDisabled()
  })
})
