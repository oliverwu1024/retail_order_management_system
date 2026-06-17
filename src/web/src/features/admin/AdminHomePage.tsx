import { Link } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { ROLE_SETS, hasAnyRole } from '@/lib/auth/roleSets'
import { useAuthStore } from '@/lib/store/auth-store'

/**
 * Admin dashboard — the /admin index, rendered inside <AdminShell />. Cards are gated by role (via
 * the same ROLE_SETS the sidebar uses) so the dashboard never advertises an area the user's role
 * can't actually enter.
 */
export function AdminHomePage() {
  const roles = useAuthStore((state) => state.user?.roles)
  const canManageCatalog = hasAnyRole(roles, ROLE_SETS.catalog)

  return (
    <section className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Dashboard</h1>
        <p className="text-sm text-muted-foreground">
          Back-office operations. The sidebar shows the areas your role can access.
        </p>
      </div>

      {canManageCatalog ? (
        <Card>
          <CardHeader>
            <CardTitle>Catalogue</CardTitle>
            <CardDescription>Create and manage products, variants, and images.</CardDescription>
          </CardHeader>
          <CardContent>
            <Button asChild>
              <Link to="/admin/products">Manage products</Link>
            </Button>
          </CardContent>
        </Card>
      ) : null}
    </section>
  )
}
