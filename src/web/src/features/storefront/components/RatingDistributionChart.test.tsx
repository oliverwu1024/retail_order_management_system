import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { RatingDistributionChart } from './RatingDistributionChart'

describe('RatingDistributionChart', () => {
  it('renders the average and review count', () => {
    render(
      <RatingDistributionChart
        summary={{ average: 4.2, count: 5, distribution: [0, 1, 0, 1, 3] }}
      />,
    )
    expect(screen.getByText('4.2')).toBeInTheDocument()
    expect(screen.getByText(/5 reviews/)).toBeInTheDocument()
  })

  it('shows an empty message when there are no reviews', () => {
    render(
      <RatingDistributionChart summary={{ average: 0, count: 0, distribution: [0, 0, 0, 0, 0] }} />,
    )
    expect(screen.getByText(/be the first to review/i)).toBeInTheDocument()
  })
})
