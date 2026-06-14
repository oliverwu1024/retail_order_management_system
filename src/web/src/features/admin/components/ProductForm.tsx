import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import type { Category } from '@/lib/api/types'
import { productFormSchema, type ProductFormValues } from '../lib/product-schema'

interface ProductFormProps {
  mode: 'create' | 'edit'
  categories: Category[]
  defaultValues: ProductFormValues
  isSubmitting: boolean
  /** Called with validated values; the page maps them to the right request body. */
  onSubmit: (values: ProductFormValues) => void
}

// Small field error line — null when the field is valid.
function FieldError({ message }: { message?: string }) {
  if (!message) {
    return null
  }
  return <p className="text-sm text-destructive">{message}</p>
}

/**
 * The product fields form (create + edit). React Hook Form keeps inputs
 * uncontrolled (one ref per field, no re-render per keystroke) and zodResolver
 * runs productFormSchema on submit, surfacing inline errors. SKU is immutable,
 * so it renders disabled in edit mode and the page omits it from the update body.
 */
export function ProductForm({
  mode,
  categories,
  defaultValues,
  isSubmitting,
  onSubmit,
}: ProductFormProps) {
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<ProductFormValues>({
    resolver: zodResolver(productFormSchema),
    defaultValues,
  })

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-5">
      <div className="grid gap-5 sm:grid-cols-2">
        <div className="space-y-1">
          <label htmlFor="sku" className="text-sm font-medium">
            SKU
          </label>
          <Input id="sku" {...register('sku')} disabled={mode === 'edit'} />
          {mode === 'edit' ? (
            <p className="text-xs text-muted-foreground">SKU can’t be changed after creation.</p>
          ) : null}
          <FieldError message={errors.sku?.message} />
        </div>

        <div className="space-y-1">
          <label htmlFor="name" className="text-sm font-medium">
            Name
          </label>
          <Input id="name" {...register('name')} />
          <FieldError message={errors.name?.message} />
        </div>

        <div className="space-y-1">
          <label htmlFor="brandName" className="text-sm font-medium">
            Brand
          </label>
          <Input id="brandName" {...register('brandName')} />
          <FieldError message={errors.brandName?.message} />
        </div>

        <div className="space-y-1">
          <label htmlFor="categoryId" className="text-sm font-medium">
            Category
          </label>
          <Select id="categoryId" {...register('categoryId')}>
            <option value="">Select a category…</option>
            {categories.map((category) => (
              <option key={category.id} value={category.id ?? ''}>
                {category.name}
              </option>
            ))}
          </Select>
          <FieldError message={errors.categoryId?.message} />
        </div>

        <div className="space-y-1">
          <label htmlFor="slug" className="text-sm font-medium">
            Slug
          </label>
          <Input id="slug" placeholder="auto-generated from name if blank" {...register('slug')} />
          <FieldError message={errors.slug?.message} />
        </div>
      </div>

      <div className="space-y-1">
        <label htmlFor="description" className="text-sm font-medium">
          Description
        </label>
        <Textarea id="description" rows={4} {...register('description')} />
        <FieldError message={errors.description?.message} />
      </div>

      <div className="grid gap-5 sm:grid-cols-2">
        <div className="space-y-1">
          <label htmlFor="seoTitle" className="text-sm font-medium">
            SEO title
          </label>
          <Input id="seoTitle" {...register('seoTitle')} />
          <FieldError message={errors.seoTitle?.message} />
        </div>

        <div className="space-y-1">
          <label htmlFor="seoDescription" className="text-sm font-medium">
            SEO description
          </label>
          <Input id="seoDescription" {...register('seoDescription')} />
          <FieldError message={errors.seoDescription?.message} />
        </div>
      </div>

      <label className="flex items-center gap-2 text-sm font-medium">
        <input
          type="checkbox"
          className="h-4 w-4 rounded border-input"
          {...register('isPublished')}
        />
        Published (visible on the storefront)
      </label>

      <Button type="submit" disabled={isSubmitting}>
        {isSubmitting ? 'Saving…' : mode === 'create' ? 'Create product' : 'Save changes'}
      </Button>
    </form>
  )
}
