import { cn } from '@/lib/utils'

// Accessible 1–5 star rating. Two modes from one component:
//   • read-only (no onChange) → a labelled row of stars for display (review cards, averages).
//   • interactive (onChange)  → real radio inputs (visually hidden), so keyboard + screen-reader
//     support come for free and React Hook Form can drive it via a Controller (value/onChange).

interface RatingStarsProps {
  /** Current rating (0 = none selected). Fractions render as the rounded number of filled stars (display only). */
  value: number
  /** Total stars. */
  max?: number
  /** When provided, the control is interactive and calls back with the chosen 1..max value. */
  onChange?: (value: number) => void
  /** Radio group name — required when interactive so the inputs group correctly. */
  name?: string
  /** Accessible group label (interactive) — defaults to "Your rating". */
  label?: string
  size?: 'sm' | 'md'
  className?: string
}

function Star({ filled, className }: { filled: boolean; className?: string }) {
  return (
    <svg
      viewBox="0 0 20 20"
      aria-hidden="true"
      fill={filled ? 'currentColor' : 'none'}
      stroke="currentColor"
      strokeWidth={filled ? 0 : 1.5}
      className={className}
    >
      <path d="M10 1.6l2.47 5.01 5.53.8-4 3.9.94 5.5L10 14.9l-4.95 2.6.94-5.5-4-3.9 5.53-.8z" />
    </svg>
  )
}

export function RatingStars({
  value,
  max = 5,
  onChange,
  name,
  label = 'Your rating',
  size = 'md',
  className,
}: RatingStarsProps) {
  const stars = Array.from({ length: max }, (_, index) => index + 1)
  const starSize = size === 'sm' ? 'size-4' : 'size-5'
  const rounded = Math.round(value)

  // ── Read-only display ────────────────────────────────────────────────────
  if (!onChange) {
    return (
      <span
        role="img"
        aria-label={`Rated ${value} out of ${max}`}
        className={cn('inline-flex items-center gap-0.5', className)}
      >
        {stars.map((star) => (
          <Star
            key={star}
            filled={star <= rounded}
            className={cn(
              starSize,
              star <= rounded ? 'text-amber-400' : 'text-muted-foreground/30',
            )}
          />
        ))}
      </span>
    )
  }

  // ── Interactive (native radios for built-in a11y + keyboard) ──────────────
  return (
    <div
      role="radiogroup"
      aria-label={label}
      className={cn('inline-flex items-center gap-1', className)}
    >
      {stars.map((star) => (
        <label
          key={star}
          className="cursor-pointer rounded p-0.5 focus-within:outline-none focus-within:ring-2 focus-within:ring-ring"
        >
          <input
            type="radio"
            name={name}
            value={star}
            checked={value === star}
            onChange={() => onChange(star)}
            className="sr-only"
          />
          <Star
            filled={star <= value}
            className={cn(
              starSize,
              'transition-colors',
              star <= value ? 'text-amber-400' : 'text-muted-foreground/40 hover:text-amber-300',
            )}
          />
          <span className="sr-only">{star === 1 ? '1 star' : `${star} stars`}</span>
        </label>
      ))}
    </div>
  )
}
