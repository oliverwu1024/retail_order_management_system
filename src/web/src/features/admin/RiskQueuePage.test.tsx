import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { RiskQueuePage } from './RiskQueuePage'

// Mock the data hooks so the page renders without a backend.
const listMock = vi.fn()
const ackMock = vi.fn()
vi.mock('./hooks/useRiskQueue', () => ({
  useRiskQueueQuery: () => listMock(),
  useAcknowledgeAnomaly: () => ackMock(),
}))

afterEach(() => {
  listMock.mockReset()
  ackMock.mockReset()
})

const PAGE = {
  items: [
    {
      id: 'a1',
      orderId: 'o1',
      orderNumber: 10392,
      score: 7.2,
      reason: 'Order total 7.2σ from the customer mean',
      detectedAt: '2026-06-20T00:00:00Z',
      acknowledged: false,
    },
  ],
  page: 1,
  totalPages: 1,
  hasPrevious: false,
  hasNext: false,
}

describe('RiskQueuePage', () => {
  it('lists flagged orders and acknowledges one', async () => {
    const mutate = vi.fn()
    listMock.mockReturnValue({ data: PAGE, isLoading: false, isError: false })
    ackMock.mockReturnValue({ mutate, isPending: false, isError: false })

    render(<RiskQueuePage />)
    expect(screen.getByText('#10392')).toBeInTheDocument()
    expect(screen.getByText(/from the customer mean/i)).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Acknowledge' }))
    expect(mutate).toHaveBeenCalledWith('a1')
  })

  it('shows the empty state when nothing is flagged', () => {
    listMock.mockReturnValue({
      data: { items: [], page: 1, totalPages: 1, hasPrevious: false, hasNext: false },
      isLoading: false,
      isError: false,
    })
    ackMock.mockReturnValue({ mutate: vi.fn(), isPending: false, isError: false })

    render(<RiskQueuePage />)
    expect(screen.getByText(/no flagged orders/i)).toBeInTheDocument()
  })
})
