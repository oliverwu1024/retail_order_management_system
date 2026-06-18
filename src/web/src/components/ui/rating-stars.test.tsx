import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { RatingStars } from './rating-stars'

describe('RatingStars', () => {
  it('renders an accessible label in read-only mode', () => {
    render(<RatingStars value={4} />)
    expect(screen.getByLabelText('Rated 4 out of 5')).toBeInTheDocument()
  })

  it('renders a radiogroup with one radio per star when interactive', () => {
    render(<RatingStars value={0} onChange={() => {}} name="rating" />)
    expect(screen.getByRole('radiogroup')).toBeInTheDocument()
    expect(screen.getAllByRole('radio')).toHaveLength(5)
  })

  it('calls onChange with the chosen star value', () => {
    const onChange = vi.fn()
    render(<RatingStars value={0} onChange={onChange} name="rating" />)
    fireEvent.click(screen.getAllByRole('radio')[2]) // the 3rd star
    expect(onChange).toHaveBeenCalledWith(3)
  })
})
