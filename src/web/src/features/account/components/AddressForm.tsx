import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { addressFormSchema, type AddressFormValues } from '../lib/account-schema'

interface AddressFormProps {
  defaultValues: AddressFormValues
  isSubmitting: boolean
  submitLabel: string
  onSubmit: (values: AddressFormValues) => void
  onCancel: () => void
}

function FieldError({ message }: { message?: string }) {
  return message ? <p className="text-xs text-destructive">{message}</p> : null
}

/**
 * Add/edit form for a single address (React Hook Form + zod). The two default
 * checkboxes map to the API's IsDefaultShipping / IsDefaultBilling flags; the
 * server clears any prior default for the chosen axis on save.
 */
export function AddressForm({
  defaultValues,
  isSubmitting,
  submitLabel,
  onSubmit,
  onCancel,
}: AddressFormProps) {
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<AddressFormValues>({
    resolver: zodResolver(addressFormSchema),
    defaultValues,
  })

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-4 rounded-md border p-4">
      <div className="space-y-1">
        <label htmlFor="line1" className="text-xs font-medium">
          Address line 1
        </label>
        <Input id="line1" {...register('line1')} />
        <FieldError message={errors.line1?.message} />
      </div>

      <div className="space-y-1">
        <label htmlFor="line2" className="text-xs font-medium">
          Address line 2
        </label>
        <Input id="line2" placeholder="optional" {...register('line2')} />
        <FieldError message={errors.line2?.message} />
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <div className="space-y-1">
          <label htmlFor="city" className="text-xs font-medium">
            City
          </label>
          <Input id="city" {...register('city')} />
          <FieldError message={errors.city?.message} />
        </div>
        <div className="space-y-1">
          <label htmlFor="region" className="text-xs font-medium">
            State / region
          </label>
          <Input id="region" placeholder="optional" {...register('region')} />
          <FieldError message={errors.region?.message} />
        </div>
        <div className="space-y-1">
          <label htmlFor="postalCode" className="text-xs font-medium">
            Postal code
          </label>
          <Input id="postalCode" {...register('postalCode')} />
          <FieldError message={errors.postalCode?.message} />
        </div>
        <div className="space-y-1">
          <label htmlFor="country" className="text-xs font-medium">
            Country (2-letter)
          </label>
          <Input id="country" placeholder="AU" maxLength={2} {...register('country')} />
          <FieldError message={errors.country?.message} />
        </div>
      </div>

      <div className="flex flex-col gap-2 sm:flex-row sm:gap-6">
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            className="h-4 w-4 rounded border-input"
            {...register('isDefaultShipping')}
          />
          Default shipping
        </label>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            className="h-4 w-4 rounded border-input"
            {...register('isDefaultBilling')}
          />
          Default billing
        </label>
      </div>

      <div className="flex gap-2">
        <Button type="submit" size="sm" disabled={isSubmitting}>
          {isSubmitting ? 'Saving…' : submitLabel}
        </Button>
        <Button
          type="button"
          size="sm"
          variant="outline"
          onClick={onCancel}
          disabled={isSubmitting}
        >
          Cancel
        </Button>
      </div>
    </form>
  )
}
