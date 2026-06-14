import { Input } from '@/components/ui/input'
import { Select } from '@/components/ui/select'
import { useCategoriesQuery } from '../hooks/useCategoriesQuery'

interface FilterPanelProps {
  search: string
  categoryId: string
  onSearchChange: (value: string) => void
  onCategoryChange: (value: string) => void
}

/** Search box + category dropdown for the catalogue listing. */
export function FilterPanel({
  search,
  categoryId,
  onSearchChange,
  onCategoryChange,
}: FilterPanelProps) {
  const { data: categories } = useCategoriesQuery()

  return (
    <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
      <Input
        type="search"
        placeholder="Search products…"
        value={search}
        onChange={(event) => onSearchChange(event.target.value)}
        className="sm:max-w-xs"
      />
      <Select
        value={categoryId}
        onChange={(event) => onCategoryChange(event.target.value)}
        className="sm:max-w-xs"
        aria-label="Filter by category"
      >
        <option value="">All categories</option>
        {categories?.map((category) => (
          <option key={category.id} value={category.id}>
            {category.name}
          </option>
        ))}
      </Select>
    </div>
  )
}
