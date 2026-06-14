import { Select } from '@/components/ui/select'
import type { ProductVariant } from '@/lib/api/types'

function variantLabel(variant: ProductVariant): string {
  const options = Object.entries(variant.options ?? {})
    .map(([key, value]) => `${key}: ${value}`)
    .join(', ')
  return options ? `${variant.sku} — ${options}` : (variant.sku ?? '')
}

interface VariantSelectorProps {
  variants: ProductVariant[]
  selectedId: string
  onSelect: (variantId: string) => void
}

/** Dropdown to pick a variant (Size/Color). Inactive variants are disabled. */
export function VariantSelector({ variants, selectedId, onSelect }: VariantSelectorProps) {
  return (
    <Select
      value={selectedId}
      onChange={(event) => onSelect(event.target.value)}
      aria-label="Choose a variant"
    >
      {variants.map((variant) => (
        <option key={variant.id} value={variant.id} disabled={!variant.isActive}>
          {variantLabel(variant)}
        </option>
      ))}
    </Select>
  )
}
