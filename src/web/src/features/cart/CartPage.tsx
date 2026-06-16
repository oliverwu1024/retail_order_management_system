import { Link } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { useStartCheckout } from '@/features/checkout/hooks/useStartCheckout'
import { toast } from '@/hooks/use-toast'
import { CartLineItem } from './components/CartLineItem'
import { CartSummary } from './components/CartSummary'
import { useClearCart, useRemoveCartItem, useUpdateCartItem } from './hooks/useCartMutations'
import { useCartQuery } from './hooks/useCartQuery'

export function CartPage() {
  const { data: cart, isLoading, isError } = useCartQuery()
  const updateItem = useUpdateCartItem()
  const removeItem = useRemoveCartItem()
  const clearCart = useClearCart()
  const startCheckout = useStartCheckout()

  // Any in-flight mutation disables the controls so a row can't be double-submitted.
  const busy =
    updateItem.isPending || removeItem.isPending || clearCart.isPending || startCheckout.isPending

  if (isLoading) {
    return <CartSkeleton />
  }

  if (isError) {
    return <p className="text-sm text-destructive">Failed to load your cart. Please refresh.</p>
  }

  const items = cart?.items ?? []

  if (items.length === 0) {
    return (
      <div className="space-y-4">
        <h1 className="text-2xl font-semibold">Your cart</h1>
        <p className="text-muted-foreground">Your cart is empty.</p>
        <Button asChild>
          <Link to="/">Browse the catalog</Link>
        </Button>
      </div>
    )
  }

  function handleCheckout() {
    startCheckout.mutate(undefined, {
      onSuccess: (url) => window.location.assign(url),
      onError: (error) =>
        notifyError(error instanceof Error ? error.message : 'Could not start checkout.'),
    })
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Your cart</h1>
        <Button
          variant="ghost"
          size="sm"
          disabled={busy}
          onClick={() =>
            clearCart.mutate(undefined, { onError: () => notifyError('Could not clear the cart.') })
          }
        >
          Clear cart
        </Button>
      </div>

      <div className="grid gap-8 md:grid-cols-[1fr_20rem]">
        <div>
          {items.map((item) => (
            <CartLineItem
              key={item.productVariantId}
              item={item}
              busy={busy}
              onChangeQuantity={(quantity) =>
                updateItem.mutate(
                  { productVariantId: item.productVariantId ?? '', quantity },
                  { onError: () => notifyError('Could not update the quantity.') },
                )
              }
              onRemove={() =>
                removeItem.mutate(item.productVariantId ?? '', {
                  onError: () => notifyError('Could not remove the item.'),
                })
              }
            />
          ))}
        </div>

        <CartSummary
          subtotalCents={cart?.subtotalCents ?? 0}
          totalQuantity={cart?.totalQuantity ?? 0}
          onCheckout={handleCheckout}
          isCheckingOut={startCheckout.isPending}
        />
      </div>
    </div>
  )
}

function notifyError(message: string) {
  toast({ variant: 'destructive', title: 'Cart error', description: message })
}

function CartSkeleton() {
  return (
    <div className="space-y-4">
      <Skeleton className="h-8 w-40" />
      <Skeleton className="h-24 w-full" />
      <Skeleton className="h-24 w-full" />
    </div>
  )
}
