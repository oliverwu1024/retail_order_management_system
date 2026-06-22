import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ForecastPage } from './ForecastPage'

// Mock the data hooks so the page renders without a backend.
const forecastMock = vi.fn()
const reorderMock = vi.fn()
const dismissMock = vi.fn()
vi.mock('./hooks/useForecast', () => ({
  useForecastQuery: () => forecastMock(),
  useReorderHintsQuery: () => reorderMock(),
  useDismissReorderHint: () => dismissMock(),
}))

// Recharts' ResponsiveContainer needs ResizeObserver (absent in jsdom) — stub it to a plain div.
vi.mock('recharts', async (importOriginal) => {
  const actual = await importOriginal<typeof import('recharts')>()
  return {
    ...actual,
    ResponsiveContainer: ({ children }: { children?: ReactNode }) => <div>{children}</div>,
  }
})

afterEach(() => {
  forecastMock.mockReset()
  reorderMock.mockReset()
  dismissMock.mockReset()
})

const page = { page: 1, totalPages: 1, hasPrevious: false, hasNext: false }
const FORECASTS = {
  items: [
    {
      productVariantId: 'v1',
      sku: 'AERO-1-M',
      productName: 'Aero Runner',
      forecastedQty: 56,
      lowerBound: 0,
      upperBound: 120,
      confidence: 1,
      generatedAt: '2026-06-22T00:00:00Z',
    },
  ],
  ...page,
}
const HINTS = {
  items: [
    {
      id: 'h1',
      productVariantId: 'v1',
      sku: 'AERO-1-M',
      productName: 'Aero Runner',
      recommendedOrderQty: 40,
      reasoning: '14-day demand 56 + safety 10, on-hand 26',
      generatedAt: '2026-06-22T00:00:00Z',
    },
  ],
  ...page,
}

describe('ForecastPage', () => {
  it('renders the reorder hints and dismisses one', async () => {
    const mutate = vi.fn()
    forecastMock.mockReturnValue({ data: FORECASTS, isLoading: false, isError: false })
    reorderMock.mockReturnValue({ data: HINTS, isLoading: false, isError: false })
    dismissMock.mockReturnValue({ mutate, isPending: false, isError: false })

    render(<ForecastPage />)
    expect(screen.getByText('Demand forecast')).toBeInTheDocument()
    expect(screen.getByText('14-day demand 56 + safety 10, on-hand 26')).toBeInTheDocument()

    await userEvent.click(
      screen.getByRole('button', { name: /dismiss reorder hint for AERO-1-M/i }),
    )
    expect(mutate).toHaveBeenCalledWith('h1')
  })

  it('shows warming-up when there are no forecasts', () => {
    forecastMock.mockReturnValue({ data: { items: [], ...page }, isLoading: false, isError: false })
    reorderMock.mockReturnValue({ data: { items: [], ...page }, isLoading: false, isError: false })
    dismissMock.mockReturnValue({ mutate: vi.fn(), isPending: false, isError: false })

    render(<ForecastPage />)
    expect(screen.getByText(/forecast warming up/i)).toBeInTheDocument()
  })
})
