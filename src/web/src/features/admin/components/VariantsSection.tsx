import { useState } from 'react'
import { useForm, useFieldArray } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Modal } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { toast } from '@/hooks/use-toast'
import type { ProductVariant } from '@/lib/api/types'
import { formatCents } from '@/lib/format'
import {
  useAddVariant,
  useAdjustInventory,
  useDeactivateVariant,
  useReactivateVariant,
} from '../hooks/useVariantMutations'
import {
  toCreateVariantBody,
  variantFormSchema,
  type VariantFormValues,
} from '../lib/product-schema'

interface VariantsSectionProps {
  productId: string
  variants: ProductVariant[]
}

// Fresh add-variant form state. Numbers default to 0; options start empty and
// grow via the field array below.
const emptyVariant: VariantFormValues = {
  sku: '',
  priceDollars: 0,
  compareAtDollars: undefined,
  initialStock: 0,
  options: [],
}

/** Renders an options map ({ Size: "M", Color: "Red" }) as a compact label. */
function formatOptions(options: ProductVariant['options']): string {
  if (!options) {
    return '—'
  }
  const parts = Object.entries(options).map(([key, value]) => `${key}: ${value}`)
  return parts.length > 0 ? parts.join(', ') : '—'
}

/**
 * Variant management for the edit page: a table of existing variants (deactivate /
 * reactivate) plus an inline add form. Variants are never hard-deleted — orders and
 * carts reference them — so "removing" one deactivates it (hidden from sale, row kept)
 * and it can be reactivated. Options are an arbitrary key/value map (Size → M, Color →
 * Red), so the add form uses useFieldArray to add/remove option rows dynamically before
 * they're folded into the request's options map.
 */
export function VariantsSection({ productId, variants }: VariantsSectionProps) {
  const addVariant = useAddVariant()
  const deactivateVariant = useDeactivateVariant()
  const reactivateVariant = useReactivateVariant()
  const mutating = deactivateVariant.isPending || reactivateVariant.isPending
  const [adjustTarget, setAdjustTarget] = useState<ProductVariant | null>(null)

  const {
    register,
    control,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<VariantFormValues>({
    resolver: zodResolver(variantFormSchema),
    defaultValues: emptyVariant,
  })

  const { fields, append, remove } = useFieldArray({ control, name: 'options' })

  function onAdd(values: VariantFormValues) {
    addVariant.mutate(
      { productId, body: toCreateVariantBody(values) },
      {
        onSuccess: () => {
          reset(emptyVariant)
          toast({ title: 'Variant added' })
        },
        onError: () =>
          toast({
            variant: 'destructive',
            title: 'Couldn’t add variant',
            description: 'Check the SKU is unique and try again.',
          }),
      },
    )
  }

  function onDeactivate(variantId: string) {
    deactivateVariant.mutate(
      { productId, variantId },
      {
        onSuccess: () => toast({ title: 'Variant deactivated' }),
        onError: () => toast({ variant: 'destructive', title: 'Couldn’t deactivate variant' }),
      },
    )
  }

  function onReactivate(variant: ProductVariant) {
    reactivateVariant.mutate(
      { productId, variant },
      {
        onSuccess: () => toast({ title: 'Variant reactivated' }),
        onError: () => toast({ variant: 'destructive', title: 'Couldn’t reactivate variant' }),
      },
    )
  }

  return (
    <section className="space-y-4">
      <h2 className="text-lg font-semibold">Variants</h2>

      {variants.length > 0 ? (
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="border-b bg-muted/50 text-left text-muted-foreground">
              <tr>
                <th className="px-3 py-2 font-medium">SKU</th>
                <th className="px-3 py-2 font-medium">Options</th>
                <th className="px-3 py-2 font-medium">Price</th>
                <th className="px-3 py-2 font-medium">Stock</th>
                <th className="px-3 py-2 font-medium">Status</th>
                <th className="px-3 py-2 font-medium">Active</th>
                <th className="px-3 py-2" />
              </tr>
            </thead>
            <tbody>
              {variants.map((variant) => {
                const inactive = variant.isActive === false
                return (
                  <tr
                    key={variant.id}
                    className={`border-b last:border-0 ${inactive ? 'opacity-60' : ''}`}
                  >
                    <td className="px-3 py-2 font-mono text-xs">{variant.sku}</td>
                    <td className="px-3 py-2">{formatOptions(variant.options)}</td>
                    <td className="px-3 py-2">{formatCents(variant.priceCents ?? 0)}</td>
                    <td className="px-3 py-2">{variant.available ?? 0}</td>
                    <td className="px-3 py-2">{variant.stockStatus ?? '—'}</td>
                    <td className="px-3 py-2">
                      <Badge variant={inactive ? 'secondary' : 'success'}>
                        {inactive ? 'Inactive' : 'Active'}
                      </Badge>
                    </td>
                    <td className="px-3 py-2 text-right">
                      {inactive ? (
                        <Button
                          type="button"
                          variant="outline"
                          size="sm"
                          disabled={mutating}
                          onClick={() => onReactivate(variant)}
                        >
                          Reactivate
                        </Button>
                      ) : (
                        <div className="flex justify-end gap-1">
                          <Button
                            type="button"
                            variant="outline"
                            size="sm"
                            disabled={!variant.id}
                            onClick={() => setAdjustTarget(variant)}
                          >
                            Adjust
                          </Button>
                          <Button
                            type="button"
                            variant="ghost"
                            size="sm"
                            disabled={mutating || !variant.id}
                            onClick={() => variant.id && onDeactivate(variant.id)}
                          >
                            Deactivate
                          </Button>
                        </div>
                      )}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">No variants yet — add one below.</p>
      )}

      {/* Add-variant form. Its own RHF instance so it validates independently of the product form. */}
      <form onSubmit={handleSubmit(onAdd)} className="space-y-4 rounded-md border p-4">
        <h3 className="text-sm font-medium">Add a variant</h3>

        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <div className="space-y-1">
            <label htmlFor="variant-sku" className="text-xs font-medium">
              SKU
            </label>
            <Input id="variant-sku" {...register('sku')} />
            {errors.sku ? <p className="text-xs text-destructive">{errors.sku.message}</p> : null}
          </div>
          <div className="space-y-1">
            <label htmlFor="variant-price" className="text-xs font-medium">
              Price ($)
            </label>
            <Input
              id="variant-price"
              type="number"
              step="0.01"
              min="0"
              {...register('priceDollars')}
            />
            {errors.priceDollars ? (
              <p className="text-xs text-destructive">{errors.priceDollars.message}</p>
            ) : null}
          </div>
          <div className="space-y-1">
            <label htmlFor="variant-compare" className="text-xs font-medium">
              Compare-at ($)
            </label>
            <Input
              id="variant-compare"
              type="number"
              step="0.01"
              min="0"
              placeholder="optional"
              {...register('compareAtDollars')}
            />
            {errors.compareAtDollars ? (
              <p className="text-xs text-destructive">{errors.compareAtDollars.message}</p>
            ) : null}
          </div>
          <div className="space-y-1">
            <label htmlFor="variant-stock" className="text-xs font-medium">
              Initial stock
            </label>
            <Input
              id="variant-stock"
              type="number"
              step="1"
              min="0"
              {...register('initialStock')}
            />
            {errors.initialStock ? (
              <p className="text-xs text-destructive">{errors.initialStock.message}</p>
            ) : null}
          </div>
        </div>

        <div className="space-y-2">
          <span className="text-xs font-medium">Options (e.g. Size → M)</span>
          {fields.map((field, index) => (
            <div key={field.id} className="flex items-center gap-2">
              <Input
                placeholder="name"
                className="max-w-40"
                {...register(`options.${index}.key`)}
              />
              <Input
                placeholder="value"
                className="max-w-40"
                {...register(`options.${index}.value`)}
              />
              <Button type="button" variant="ghost" size="sm" onClick={() => remove(index)}>
                Remove
              </Button>
            </div>
          ))}
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => append({ key: '', value: '' })}
          >
            Add option
          </Button>
        </div>

        <Button type="submit" disabled={addVariant.isPending}>
          {addVariant.isPending ? 'Adding…' : 'Add variant'}
        </Button>
      </form>

      {adjustTarget ? (
        <AdjustStockModal
          productId={productId}
          variant={adjustTarget}
          onClose={() => setAdjustTarget(null)}
        />
      ) : null}
    </section>
  )
}

/**
 * Stock-adjustment modal: a signed delta + a reason, posted to the inventory-adjust endpoint.
 * The delta must be a non-zero integer and the reason non-empty (mirrors the server validator);
 * the reason is recorded in the audit log.
 */
function AdjustStockModal({
  productId,
  variant,
  onClose,
}: {
  productId: string
  variant: ProductVariant
  onClose: () => void
}) {
  const adjustInventory = useAdjustInventory()
  const [delta, setDelta] = useState('')
  const [reason, setReason] = useState('')

  const parsedDelta = Number(delta)
  const valid =
    delta.trim() !== '' &&
    Number.isInteger(parsedDelta) &&
    parsedDelta !== 0 &&
    reason.trim().length > 0

  function onSubmit() {
    if (!valid || !variant.id) {
      return
    }
    adjustInventory.mutate(
      { productId, variantId: variant.id, delta: parsedDelta, reason: reason.trim() },
      {
        onSuccess: (stock) => {
          toast({ title: 'Stock adjusted', description: `On-hand is now ${stock.onHand}.` })
          onClose()
        },
        onError: () => toast({ variant: 'destructive', title: 'Couldn’t adjust stock' }),
      },
    )
  }

  return (
    <Modal
      open
      onOpenChange={(isOpen) => {
        if (!isOpen) onClose()
      }}
      title={`Adjust stock · ${variant.sku}`}
      description="Apply a signed change to on-hand stock. The reason is recorded in the audit log."
    >
      <div className="space-y-4">
        <div className="space-y-1">
          <label htmlFor="adjust-delta" className="text-xs font-medium">
            Delta (e.g. 10 or -3)
          </label>
          <Input
            id="adjust-delta"
            type="number"
            step="1"
            value={delta}
            onChange={(e) => setDelta(e.target.value)}
          />
        </div>
        <div className="space-y-1">
          <label htmlFor="adjust-reason" className="text-xs font-medium">
            Reason
          </label>
          <Input
            id="adjust-reason"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            placeholder="Cycle count, damage, restock…"
          />
        </div>
        <div className="flex justify-end gap-2">
          <Button
            type="button"
            variant="ghost"
            onClick={onClose}
            disabled={adjustInventory.isPending}
          >
            Cancel
          </Button>
          <Button type="button" onClick={onSubmit} disabled={!valid || adjustInventory.isPending}>
            {adjustInventory.isPending ? 'Saving…' : 'Apply'}
          </Button>
        </div>
      </div>
    </Modal>
  )
}
