import { Link, Outlet, useNavigate } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { useSessionActions } from '@/features/auth/useSessionActions'
import { useCartQuery } from '@/features/cart/hooks/useCartQuery'
import { apiClient } from '@/lib/api/client'
import { useAuthStore } from '@/lib/store/auth-store'

// Administrator-only for now — matches the Administrator-only backend admin endpoints
// (StoreManager gets backed admin routes when the Phase 3 RBAC matrix lands).
const ADMIN_ROLES = ['Administrator']

/** Storefront layout: auth-aware header + routed content (React Router layout route). */
export function StorefrontShell() {
  const navigate = useNavigate()
  const { signOut } = useSessionActions()
  const user = useAuthStore((state) => state.user)
  const isLoading = useAuthStore((state) => state.isLoading)
  const canAdmin = user?.roles.some((role) => ADMIN_ROLES.includes(role)) ?? false
  const isCustomer = user?.roles.includes('Customer') ?? false
  // The cart badge shows for everyone (guests included); shares the cart cache with /cart.
  const { data: cart } = useCartQuery()
  const cartCount = cart?.totalQuantity ?? 0

  async function handleSignOut() {
    await apiClient.POST('/api/v1/auth/logout')
    signOut()
    navigate('/')
  }

  return (
    <div className="min-h-screen bg-background text-foreground">
      <header className="border-b">
        <div className="mx-auto flex max-w-6xl items-center justify-between px-4 py-4">
          <Link to="/" className="text-lg font-semibold tracking-tight">
            Retail OMS
          </Link>
          <nav className="flex items-center gap-4 text-sm">
            <Link to="/" className="text-muted-foreground hover:text-foreground">
              Catalog
            </Link>
            <Link to="/cart" className="text-muted-foreground hover:text-foreground">
              Cart
              {cartCount > 0 ? (
                <span className="ml-1 inline-flex min-w-5 items-center justify-center rounded-full bg-primary px-1.5 py-0.5 text-xs font-medium text-primary-foreground">
                  {cartCount}
                </span>
              ) : null}
            </Link>
            {!isLoading && canAdmin ? (
              <Link to="/admin" className="text-muted-foreground hover:text-foreground">
                Admin
              </Link>
            ) : null}
            {!isLoading && isCustomer ? (
              <>
                <Link to="/orders" className="text-muted-foreground hover:text-foreground">
                  Orders
                </Link>
                <Link to="/account" className="text-muted-foreground hover:text-foreground">
                  Account
                </Link>
              </>
            ) : null}
            {isLoading ? null : user ? (
              <>
                <span className="text-muted-foreground">{user.email}</span>
                <Button variant="outline" size="sm" onClick={handleSignOut}>
                  Sign out
                </Button>
              </>
            ) : (
              <Link to="/login" className="text-muted-foreground hover:text-foreground">
                Sign in
              </Link>
            )}
          </nav>
        </div>
      </header>
      <main className="mx-auto max-w-6xl px-4 py-8">
        <Outlet />
      </main>
    </div>
  )
}
