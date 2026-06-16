import { Link, useNavigate, useParams } from 'react-router-dom'
import { Skeleton } from '@/components/ui/skeleton'
import { toast } from '@/hooks/use-toast'
import { useCategoriesQuery } from '@/features/storefront/hooks/useCategoriesQuery'
import type { ProductDetail } from '@/lib/api/types'
import { ImageGalleryManager } from './components/ImageGalleryManager'
import { ProductForm } from './components/ProductForm'
import { VariantsSection } from './components/VariantsSection'
import { useAdminProductQuery } from './hooks/useAdminProductQuery'
import { useCreateProduct, useUpdateProduct } from './hooks/useProductMutations'
import {
  toCreateProductBody,
  toUpdateProductBody,
  type ProductFormValues,
} from './lib/product-schema'

// Blank product for the create form.
const emptyProduct: ProductFormValues = {
  sku: '',
  name: '',
  slug: '',
  brandName: '',
  description: '',
  seoTitle: '',
  seoDescription: '',
  categoryId: '',
  isPublished: false,
}

// ProductDetailDto → form values (the DTO's fields are all optional in the
// generated schema, so coalesce each to the form's non-null shape).
function toFormValues(product: ProductDetail): ProductFormValues {
  return {
    sku: product.sku ?? '',
    name: product.name ?? '',
    slug: product.slug ?? '',
    brandName: product.brandName ?? '',
    description: product.description ?? '',
    seoTitle: product.seoTitle ?? '',
    seoDescription: product.seoDescription ?? '',
    categoryId: product.category?.id ?? '',
    isPublished: product.isPublished ?? false,
  }
}

/**
 * Create + edit product page. One route param decides the mode: /admin/products/new
 * has no :id (create), /admin/products/:id loads that product (edit). Image upload
 * and variant management only appear in edit mode — both endpoints are keyed by an
 * existing product id, so creating navigates here in edit mode to continue.
 */
export function ProductFormPage() {
  const { id } = useParams()
  const mode = id ? 'edit' : 'create'
  const navigate = useNavigate()

  const categoriesQuery = useCategoriesQuery()
  const productQuery = useAdminProductQuery(id)
  const createProduct = useCreateProduct()
  const updateProduct = useUpdateProduct()

  const categories = categoriesQuery.data ?? []

  function onSubmit(values: ProductFormValues) {
    if (mode === 'create') {
      createProduct.mutate(toCreateProductBody(values), {
        onSuccess: (product) => {
          toast({ title: 'Product created', description: 'Now add variants and an image.' })
          // Continue in edit mode so variants + image (which need an id) can be added.
          navigate(`/admin/products/${product.id}`)
        },
        onError: () =>
          toast({
            variant: 'destructive',
            title: 'Couldn’t create product',
            description: 'Check the SKU and slug are unique.',
          }),
      })
      return
    }

    updateProduct.mutate(
      { id: id!, body: toUpdateProductBody(values) },
      {
        onSuccess: () => toast({ title: 'Product saved' }),
        onError: () =>
          toast({
            variant: 'destructive',
            title: 'Couldn’t save product',
            description: 'Check the slug is unique and try again.',
          }),
      },
    )
  }

  const isSubmitting = createProduct.isPending || updateProduct.isPending

  return (
    <div className="mx-auto max-w-3xl space-y-8">
      <div className="space-y-1">
        <Link to="/admin/products" className="text-sm text-muted-foreground hover:text-foreground">
          ← Back to products
        </Link>
        <h1 className="text-2xl font-semibold">
          {mode === 'create' ? 'New product' : (productQuery.data?.name ?? 'Edit product')}
        </h1>
      </div>

      {mode === 'edit' && productQuery.isLoading ? (
        <div className="space-y-3">
          {Array.from({ length: 6 }).map((_, index) => (
            <Skeleton key={index} className="h-10 w-full" />
          ))}
        </div>
      ) : mode === 'edit' && productQuery.isError ? (
        <p className="text-sm text-destructive">Couldn’t load this product.</p>
      ) : (
        <>
          <ProductForm
            mode={mode}
            categories={categories}
            // key forces a fresh RHF instance once the loaded values are ready,
            // so async-loaded defaults populate the uncontrolled inputs.
            key={productQuery.data?.id ?? 'new'}
            defaultValues={productQuery.data ? toFormValues(productQuery.data) : emptyProduct}
            isSubmitting={isSubmitting}
            onSubmit={onSubmit}
          />

          {mode === 'edit' && productQuery.data ? (
            <>
              <VariantsSection
                productId={productQuery.data.id!}
                variants={productQuery.data.variants ?? []}
              />
              {/* Gallery after variants so the variant dropdown can scope images to a variant. */}
              <ImageGalleryManager
                productId={productQuery.data.id!}
                images={productQuery.data.images ?? []}
                variants={productQuery.data.variants ?? []}
              />
            </>
          ) : null}
        </>
      )}
    </div>
  )
}
