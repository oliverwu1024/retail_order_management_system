import { useState, type FormEvent } from 'react'
import { Link, Navigate, useLocation } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { apiClient } from '@/lib/api/client'
import { useAuthStore } from '@/lib/store/auth-store'
import { useSessionActions } from './useSessionActions'

export function LoginPage() {
  const location = useLocation()
  const { signIn } = useSessionActions()
  const user = useAuthStore((state) => state.user)
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  // Where to land once authenticated: back to the page a guard bounced us from, else a role-based
  // home (a customer can't see /admin, so defaulting there would just bounce them back here).
  const fromPath = (location.state as { from?: { pathname?: string } } | null)?.from?.pathname

  // DECLARATIVE redirect: the instant the auth store holds a user we leave /login. This fires both
  // for a just-completed login (signIn sets the store → re-render → here) and for an already
  // signed-in user who lands on /login. Reacting to settled state — rather than an imperative
  // navigate() in the submit handler — means it can't race the store update or be dropped, which
  // is what left users authenticated-but-stranded on /login.
  if (user) {
    const destination = fromPath ?? (user.roles.includes('Administrator') ? '/admin' : '/')
    return <Navigate to={destination} replace />
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    setSubmitting(true)
    setError('')

    try {
      const { data, error: apiError } = await apiClient.POST('/api/v1/auth/login', {
        body: { email, password },
      })

      if (apiError || !data?.data) {
        setError('Invalid email or password.')
        return
      }

      // Sets the auth store → this component re-renders and the redirect above takes over.
      signIn(data.data)
    } catch {
      // e.g. the CSRF cookie is missing → the client middleware throws. Surface it instead of
      // leaving the button stuck on "Signing in…" forever.
      setError('Something went wrong. Please refresh the page and try again.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="mx-auto max-w-sm py-10">
      <Card>
        <CardHeader>
          <CardTitle className="text-xl">Sign in</CardTitle>
        </CardHeader>
        <CardContent>
          <form onSubmit={onSubmit} className="space-y-4">
            <div className="space-y-1">
              <label htmlFor="email" className="text-sm font-medium">
                Email
              </label>
              <Input
                id="email"
                type="email"
                autoComplete="email"
                value={email}
                onChange={(event) => setEmail(event.target.value)}
                required
              />
            </div>
            <div className="space-y-1">
              <label htmlFor="password" className="text-sm font-medium">
                Password
              </label>
              <Input
                id="password"
                type="password"
                autoComplete="current-password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                required
              />
            </div>
            {error ? <p className="text-sm text-destructive">{error}</p> : null}
            <Button type="submit" className="w-full" disabled={submitting}>
              {submitting ? 'Signing in…' : 'Sign in'}
            </Button>
          </form>

          <p className="mt-4 text-center text-sm text-muted-foreground">
            Don’t have an account?{' '}
            <Link to="/register" className="text-primary hover:underline">
              Create one
            </Link>
          </p>
        </CardContent>
      </Card>
    </div>
  )
}
