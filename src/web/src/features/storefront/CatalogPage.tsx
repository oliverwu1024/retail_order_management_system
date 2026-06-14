import { useSearchParams } from 'react-router-dom'
import { Pagination } from '@/components/ui/pagination'
import { Skeleton } from '@/components/ui/skeleton'
import { FilterPanel } from './components/FilterPanel'
import { ProductCard } from './components/ProductCard'
import { useProductsQuery } from './hooks/useProductsQuery'

const PAGE_SIZE = 12

/** Storefront catalogue grid: URL-driven paging, category filter, and search. */
export function CatalogPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const page = Math.max(1, Number(searchParams.get('page')) || 1)
  const categoryId = searchParams.get('category') ?? ''
  const search = searchParams.get('q') ?? ''

  const { data, isLoading, isError } = useProductsQuery({
    page,
    pageSize: PAGE_SIZE,
    categoryId: categoryId || undefined,
    search: search || undefined,
  })

  // Filter changes reset to page 1; page changes preserve the filters.
  function setParam(key: string, value: string, resetPage: boolean) {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev)
      if (value) {
        next.set(key, value)
      } else {
        next.delete(key)
      }
      if (resetPage) {
        next.delete('page')
      }
      return next
    })
  }

  const items = data?.items ?? []

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">Products</h1>

      <FilterPanel
        search={search}
        categoryId={categoryId}
        onSearchChange={(value) => setParam('q', value, true)}
        onCategoryChange={(value) => setParam('category', value, true)}
      />

      {isError ? (
        <p className="text-sm text-destructive">Couldn’t load products. Please try again.</p>
      ) : isLoading ? (
        <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
          {Array.from({ length: 8 }).map((_, index) => (
            <Skeleton key={index} className="aspect-[3/4] w-full" />
          ))}
        </div>
      ) : items.length > 0 ? (
        <>
          <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
            {items.map((product) => (
              <ProductCard key={product.id} product={product} />
            ))}
          </div>
          {data ? (
            <Pagination
              page={data.page ?? page}
              totalPages={data.totalPages ?? 1}
              hasPrevious={data.hasPrevious ?? false}
              hasNext={data.hasNext ?? false}
              onPageChange={(next) => setParam('page', String(next), false)}
            />
          ) : null}
        </>
      ) : (
        <p className="text-sm text-muted-foreground">No products found.</p>
      )}
    </div>
  )
}
