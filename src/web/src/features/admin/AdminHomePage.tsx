import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'

// Admin dashboard — Phase 0 placeholder. Wrapped in <RoleGuard> in the
// router so unauthenticated or non-admin users are redirected before
// this renders.
export function AdminHomePage() {
  return (
    <main className="container mx-auto max-w-3xl py-10">
      <Card>
        <CardHeader>
          <CardTitle>Admin</CardTitle>
          <CardDescription>
            Phase 0 placeholder. Real dashboard lands in Phase 3 (admin foundations).
          </CardDescription>
        </CardHeader>
        <CardContent>
          <p className="text-sm">
            If you can see this page, the route guard let you through — meaning the auth store has a
            user with an Admin or StoreManager role claim.
          </p>
        </CardContent>
      </Card>
    </main>
  )
}
