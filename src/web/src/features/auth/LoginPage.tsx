import { useState, type FormEvent } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { apiClient } from '@/lib/api/client'
import { useSessionActions } from './useSessionActions'

export function LoginPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const { signIn } = useSessionActions()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  // Where to land after login — back to the page the guard bounced us from, else /admin.
  const from =
    (location.state as { from?: { pathname?: string } } | null)?.from?.pathname ?? '/admin'

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    setSubmitting(true)
    setError('')

    const { data, error: apiError } = await apiClient.POST('/api/v1/auth/login', {
      body: { email, password },
    })

    setSubmitting(false)
    if (apiError || !data?.data) {
      setError('Invalid email or password.')
      return
    }

    signIn(data.data)
    navigate(from, { replace: true })
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
