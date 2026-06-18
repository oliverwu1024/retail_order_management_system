import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { SentimentMetricsTile } from './SentimentMetricsTile'

describe('SentimentMetricsTile', () => {
  it('renders the average, scored count, and label chips', () => {
    render(
      <SentimentMetricsTile
        summary={{
          averageScore: 0.42,
          scoredReviews: 8,
          labelDistribution: [
            { label: 'Positive', count: 5 },
            { label: 'Negative', count: 3 },
          ],
          products: [],
        }}
      />,
    )
    expect(screen.getByText('0.42')).toBeInTheDocument()
    expect(screen.getByText(/8 scored/)).toBeInTheDocument()
    expect(screen.getByText('Positive: 5')).toBeInTheDocument()
  })

  it('shows a fallback when nothing is scored', () => {
    render(
      <SentimentMetricsTile
        summary={{ averageScore: null, scoredReviews: 0, labelDistribution: [], products: [] }}
      />,
    )
    expect(screen.getByText('—')).toBeInTheDocument()
    expect(screen.getByText(/no reviews scored yet/i)).toBeInTheDocument()
  })
})
