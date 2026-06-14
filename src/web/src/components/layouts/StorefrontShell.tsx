import { Link, Outlet, useNavigate } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { applyAuthUser } from '@/features/auth/session'
import { apiClient } from '@/lib/api/client'
import { useAuthStore } from '@/lib/store/auth-store'

const ADMIN_ROLES = ['Administrator', 'StoreManager']

/** Storefront layout: auth-aware header + routed content (React Router layout route). */
export function StorefrontShell() {
  const navigate = useNavigate()
  const user = useAuthStore((state) => state.user)
  const isLoading = useAuthStore((state) => state.isLoading)
  const canAdmin = user?.roles.some((role) => ADMIN_ROLES.includes(role)) ?? false
  const isCustomer = user?.roles.includes('Customer') ?? false

  async function handleSignOut() {
    await apiClient.POST('/api/v1/auth/logout')
    applyAuthUser(null)
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
            {!isLoading && canAdmin ? (
              <Link to="/admin" className="text-muted-foreground hover:text-foreground">
                Admin
              </Link>
            ) : null}
            {!isLoading && isCustomer ? (
              <Link to="/account" className="text-muted-foreground hover:text-foreground">
                Account
              </Link>
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
