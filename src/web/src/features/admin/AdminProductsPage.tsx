import { Link, useSearchParams } from 'react-router-dom'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { DataTable, type Column } from '@/components/ui/data-table'
import { EmptyState } from '@/components/ui/empty-state'
import { Pagination } from '@/components/ui/pagination'
import { Skeleton } from '@/components/ui/skeleton'
import { toast } from '@/hooks/use-toast'
import { FilterPanel } from '@/features/storefront/components/FilterPanel'
import type { ProductSummary } from '@/lib/api/types'
import { formatCents } from '@/lib/format'
import { useAdminProductsQuery } from './hooks/useAdminProductsQuery'
import { useDeleteProduct } from './hooks/useProductMutations'

const PAGE_SIZE = 20

/**
 * Admin product table: URL-driven search / category filter / paging (same pattern as the storefront,
 * but against /catalog/admin/products so drafts show too). Composed from the shared <DataTable />
 * primitive (PHASE_3_SCOPE.md §3.6 — proving reuse), with each row linking to the edit form.
 */
export function AdminProductsPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const page = Math.max(1, Number(searchParams.get('page')) || 1)
  const categoryId = searchParams.get('category') ?? ''
  const search = searchParams.get('q') ?? ''

  const { data, isLoading, isError } = useAdminProductsQuery({
    page,
    pageSize: PAGE_SIZE,
    categoryId: categoryId || undefined,
    search: search || undefined,
  })
  const deleteProduct = useDeleteProduct()

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

  function onDelete(id: string, name: string) {
    if (!window.confirm(`Delete “${name}”? It will be hidden from the storefront (recoverable).`)) {
      return
    }
    deleteProduct.mutate(id, {
      onSuccess: () => toast({ title: 'Product deleted' }),
      onError: () => toast({ variant: 'destructive', title: 'Couldn’t delete product' }),
    })
  }

  const columns: Column<ProductSummary>[] = [
    {
      key: 'name',
      header: 'Name',
      cell: (product) => (
        <Link
          to={`/admin/products/${product.id}`}
          className="font-medium text-primary hover:underline"
        >
          {product.name}
        </Link>
      ),
    },
    { key: 'sku', header: 'SKU', className: 'font-mono text-xs', cell: (product) => product.sku },
    { key: 'brand', header: 'Brand', cell: (product) => product.brandName ?? '—' },
    {
      key: 'from',
      header: 'From',
      cell: (product) =>
        product.fromPriceCents != null ? formatCents(product.fromPriceCents) : '—',
    },
    {
      key: 'status',
      header: 'Status',
      cell: (product) =>
        product.isPublished ? (
          <Badge variant="success">Published</Badge>
        ) : (
          <Badge variant="secondary">Draft</Badge>
        ),
    },
    {
      key: 'actions',
      header: '',
      className: 'text-right',
      cell: (product) => (
        <Button
          type="button"
          variant="ghost"
          size="sm"
          disabled={deleteProduct.isPending}
          onClick={() => product.id && onDelete(product.id, product.name ?? 'this product')}
        >
          Delete
        </Button>
      ),
    },
  ]

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Products</h1>
        <Button asChild>
          <Link to="/admin/products/new">New product</Link>
        </Button>
      </div>

      <FilterPanel
        search={search}
        categoryId={categoryId}
        onSearchChange={(value) => setParam('q', value, true)}
        onCategoryChange={(value) => setParam('category', value, true)}
      />

      {isError ? (
        <p className="text-sm text-destructive">Couldn’t load products. Please try again.</p>
      ) : isLoading ? (
        <div className="space-y-2">
          {Array.from({ length: 6 }).map((_, index) => (
            <Skeleton key={index} className="h-12 w-full" />
          ))}
        </div>
      ) : (
        <>
          <DataTable
            label="Products"
            columns={columns}
            rows={data?.items ?? []}
            getRowKey={(product) => product.id ?? ''}
            empty={
              <EmptyState
                title="No products found"
                description="Try a different search or filter."
              />
            }
          />
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
      )}
    </div>
  )
}
