import { Link } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'

// Admin dashboard — Phase 0 placeholder. Wrapped in <RoleGuard> in the
// router so unauthenticated or non-admin users are redirected before
// this renders. The full dashboard lands in Phase 3; for now it links to
// the product management built in Story 1.3.
export function AdminHomePage() {
  return (
    <main className="container mx-auto max-w-3xl py-10">
      <Card>
        <CardHeader>
          <CardTitle>Admin</CardTitle>
          <CardDescription>
            Manage the catalogue. The full dashboard lands in Phase 3 (admin foundations).
          </CardDescription>
        </CardHeader>
        <CardContent>
          <Button asChild>
            <Link to="/admin/products">Manage products</Link>
          </Button>
        </CardContent>
      </Card>
    </main>
  )
}
