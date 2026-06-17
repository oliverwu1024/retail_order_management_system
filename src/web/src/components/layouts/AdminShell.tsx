import { Link, Outlet, useNavigate } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { SidebarNav } from '@/features/admin/components/SidebarNav'
import { useSessionActions } from '@/features/auth/useSessionActions'
import { apiClient } from '@/lib/api/client'
import { useAuthStore } from '@/lib/store/auth-store'

/**
 * Admin back-office layout: a role-driven sidebar + a topbar + routed content. Distinct from the
 * storefront's <StorefrontShell /> — the /admin area is its own surface (PHASE_3_SCOPE.md §12), so
 * it does NOT render the storefront header.
 */
export function AdminShell() {
  const navigate = useNavigate()
  const { signOut } = useSessionActions()
  const user = useAuthStore((state) => state.user)

  async function handleSignOut() {
    await apiClient.POST('/api/v1/auth/logout')
    signOut()
    navigate('/')
  }

  return (
    <div className="min-h-screen bg-background text-foreground">
      <div className="mx-auto flex min-h-screen max-w-7xl">
        <aside className="w-56 shrink-0 border-r p-4">
          <Link to="/admin" className="mb-6 block text-lg font-semibold tracking-tight">
            Retail OMS <span className="text-muted-foreground">Admin</span>
          </Link>
          <SidebarNav />
        </aside>

        <div className="flex min-w-0 flex-1 flex-col">
          <header className="flex items-center justify-between border-b px-6 py-3">
            <Link to="/" className="text-sm text-muted-foreground hover:text-foreground">
              ← Storefront
            </Link>
            <div className="flex items-center gap-3 text-sm">
              {user ? <span className="text-muted-foreground">{user.email}</span> : null}
              <Button variant="outline" size="sm" onClick={handleSignOut}>
                Sign out
              </Button>
            </div>
          </header>
          <main className="min-w-0 flex-1 p-6">
            <Outlet />
          </main>
        </div>
      </div>
    </div>
  )
}
