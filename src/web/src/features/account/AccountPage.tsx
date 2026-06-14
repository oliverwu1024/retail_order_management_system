import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { AddressSection } from './components/AddressSection'
import { ProfileForm } from './components/ProfileForm'
import { useProfileQuery } from './hooks/useProfileQuery'

/**
 * "My Account" page (Story 1.4.2): the signed-in customer's profile + address book.
 * Guarded to the Customer role in the router. The profile query lazily creates the
 * profile server-side on first load, so there's no empty/onboarding state to handle.
 */
export function AccountPage() {
  const { data: profile, isLoading, isError } = useProfileQuery()

  return (
    <div className="mx-auto max-w-2xl space-y-6">
      <h1 className="text-2xl font-semibold">My account</h1>

      {isError ? (
        <p className="text-sm text-destructive">Couldn’t load your account. Please try again.</p>
      ) : isLoading || !profile ? (
        <div className="space-y-3">
          {Array.from({ length: 5 }).map((_, index) => (
            <Skeleton key={index} className="h-10 w-full" />
          ))}
        </div>
      ) : (
        <>
          <Card>
            <CardHeader>
              <CardTitle className="text-lg">Profile</CardTitle>
            </CardHeader>
            <CardContent>
              <ProfileForm profile={profile} />
            </CardContent>
          </Card>

          <Card>
            <CardContent className="pt-6">
              <AddressSection addresses={profile.addresses ?? []} />
            </CardContent>
          </Card>
        </>
      )}
    </div>
  )
}
