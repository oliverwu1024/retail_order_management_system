import { useState } from 'react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { toast } from '@/hooks/use-toast'
import type { Address } from '@/lib/api/types'
import { useAddAddress, useDeleteAddress, useUpdateAddress } from '../hooks/useAccountMutations'
import { toAddressBody, type AddressFormValues } from '../lib/account-schema'
import { AddressForm } from './AddressForm'

interface AddressSectionProps {
  addresses: Address[]
}

// Blank form state for "add a new address".
const emptyAddress: AddressFormValues = {
  line1: '',
  line2: '',
  city: '',
  region: '',
  postalCode: '',
  country: '',
  isDefaultShipping: false,
  isDefaultBilling: false,
}

// Address entity → form values (DTO fields are optional in the generated schema).
function toFormValues(address: Address): AddressFormValues {
  return {
    line1: address.line1 ?? '',
    line2: address.line2 ?? '',
    city: address.city ?? '',
    region: address.region ?? '',
    postalCode: address.postalCode ?? '',
    country: address.country ?? '',
    isDefaultShipping: address.isDefaultShipping ?? false,
    isDefaultBilling: address.isDefaultBilling ?? false,
  }
}

/**
 * Address book: a card per saved address with default badges + edit/delete, plus an
 * inline add/edit form. `editing` is the address id being edited, the sentinel 'new'
 * for the add form, or null when just listing.
 */
export function AddressSection({ addresses }: AddressSectionProps) {
  const [editing, setEditing] = useState<string | 'new' | null>(null)
  const addAddress = useAddAddress()
  const updateAddress = useUpdateAddress()
  const deleteAddress = useDeleteAddress()

  function onAdd(values: AddressFormValues) {
    addAddress.mutate(toAddressBody(values), {
      onSuccess: () => {
        setEditing(null)
        toast({ title: 'Address added' })
      },
      onError: () =>
        toast({
          variant: 'destructive',
          title: 'Couldn’t add address',
          description: 'Please try again.',
        }),
    })
  }

  function onUpdate(id: string, values: AddressFormValues) {
    updateAddress.mutate(
      { id, body: toAddressBody(values) },
      {
        onSuccess: () => {
          setEditing(null)
          toast({ title: 'Address saved' })
        },
        onError: () =>
          toast({
            variant: 'destructive',
            title: 'Couldn’t save address',
            description: 'Please try again.',
          }),
      },
    )
  }

  function onDelete(id: string) {
    if (!window.confirm('Delete this address?')) {
      return
    }
    deleteAddress.mutate(id, {
      onSuccess: () => toast({ title: 'Address deleted' }),
      onError: () => toast({ variant: 'destructive', title: 'Couldn’t delete address' }),
    })
  }

  return (
    <section className="space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="text-lg font-semibold">Addresses</h2>
        {editing !== 'new' ? (
          <Button size="sm" variant="outline" onClick={() => setEditing('new')}>
            Add address
          </Button>
        ) : null}
      </div>

      {editing === 'new' ? (
        <AddressForm
          defaultValues={emptyAddress}
          isSubmitting={addAddress.isPending}
          submitLabel="Add address"
          onSubmit={onAdd}
          onCancel={() => setEditing(null)}
        />
      ) : null}

      {addresses.length === 0 && editing !== 'new' ? (
        <p className="text-sm text-muted-foreground">No saved addresses yet.</p>
      ) : null}

      <div className="space-y-3">
        {addresses.map((address) =>
          editing === address.id ? (
            <AddressForm
              key={address.id}
              defaultValues={toFormValues(address)}
              isSubmitting={updateAddress.isPending}
              submitLabel="Save address"
              onSubmit={(values) => onUpdate(address.id!, values)}
              onCancel={() => setEditing(null)}
            />
          ) : (
            <div
              key={address.id}
              className="flex items-start justify-between gap-4 rounded-md border p-4"
            >
              <div className="space-y-1 text-sm">
                <div className="flex flex-wrap gap-2">
                  {address.isDefaultShipping ? (
                    <Badge variant="success">Default shipping</Badge>
                  ) : null}
                  {address.isDefaultBilling ? (
                    <Badge variant="secondary">Default billing</Badge>
                  ) : null}
                </div>
                <p>{address.line1}</p>
                {address.line2 ? <p>{address.line2}</p> : null}
                <p className="text-muted-foreground">
                  {[address.city, address.region, address.postalCode].filter(Boolean).join(', ')} ·{' '}
                  {address.country}
                </p>
              </div>
              <div className="flex shrink-0 gap-2">
                <Button size="sm" variant="ghost" onClick={() => setEditing(address.id!)}>
                  Edit
                </Button>
                <Button
                  size="sm"
                  variant="ghost"
                  disabled={deleteAddress.isPending}
                  onClick={() => address.id && onDelete(address.id)}
                >
                  Delete
                </Button>
              </div>
            </div>
          ),
        )}
      </div>
    </section>
  )
}
