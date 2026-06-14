import { Link, useSearchParams } from 'react-router-dom'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Pagination } from '@/components/ui/pagination'
import { Skeleton } from '@/components/ui/skeleton'
import { toast } from '@/hooks/use-toast'
import { FilterPanel } from '@/features/storefront/components/FilterPanel'
import { formatCents } from '@/lib/format'
import { useAdminProductsQuery } from './hooks/useAdminProductsQuery'
import { useDeleteProduct } from './hooks/useProductMutations'

const PAGE_SIZE = 20

/**
 * Admin product table: URL-driven search / category filter / paging (same
 * pattern as the storefront, but against /catalog/admin/products so drafts
 * show too). Each row links to the edit form; the published column reflects
 * storefront visibility at a glance.
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

  const items = data?.items ?? []

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
      ) : items.length > 0 ? (
        <>
          <div className="overflow-x-auto rounded-md border">
            <table className="w-full text-sm">
              <thead className="border-b bg-muted/50 text-left text-muted-foreground">
                <tr>
                  <th className="px-3 py-2 font-medium">Name</th>
                  <th className="px-3 py-2 font-medium">SKU</th>
                  <th className="px-3 py-2 font-medium">Brand</th>
                  <th className="px-3 py-2 font-medium">From</th>
                  <th className="px-3 py-2 font-medium">Status</th>
                  <th className="px-3 py-2" />
                </tr>
              </thead>
              <tbody>
                {items.map((product) => (
                  <tr key={product.id} className="border-b last:border-0">
                    <td className="px-3 py-2">
                      <Link
                        to={`/admin/products/${product.id}`}
                        className="font-medium text-primary hover:underline"
                      >
                        {product.name}
                      </Link>
                    </td>
                    <td className="px-3 py-2 font-mono text-xs">{product.sku}</td>
                    <td className="px-3 py-2">{product.brandName ?? '—'}</td>
                    <td className="px-3 py-2">
                      {product.fromPriceCents != null ? formatCents(product.fromPriceCents) : '—'}
                    </td>
                    <td className="px-3 py-2">
                      {product.isPublished ? (
                        <Badge variant="success">Published</Badge>
                      ) : (
                        <Badge variant="secondary">Draft</Badge>
                      )}
                    </td>
                    <td className="px-3 py-2 text-right">
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        disabled={deleteProduct.isPending}
                        onClick={() =>
                          product.id && onDelete(product.id, product.name ?? 'this product')
                        }
                      >
                        Delete
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
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
