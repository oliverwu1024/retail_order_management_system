import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { apiClient } from '@/lib/api/client'
import { useSessionActions } from './useSessionActions'

// Mirrors RegisterRequestValidator + the Identity password policy (REQUIREMENTS §1.1):
// valid email, a display name, and a 12+ char password with at least one letter + digit.
const registerSchema = z.object({
  email: z.string().trim().email('Enter a valid email address').max(256),
  displayName: z.string().trim().min(1, 'Display name is required').max(100),
  password: z
    .string()
    .min(12, 'Password must be at least 12 characters')
    .regex(/[A-Za-z]/, 'Password must contain at least one letter')
    .regex(/[0-9]/, 'Password must contain at least one digit'),
})

type RegisterValues = z.infer<typeof registerSchema>

export function RegisterPage() {
  const navigate = useNavigate()
  const { signIn } = useSessionActions()
  const [serverError, setServerError] = useState('')
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<RegisterValues>({
    resolver: zodResolver(registerSchema),
    defaultValues: { email: '', displayName: '', password: '' },
  })

  async function onSubmit(values: RegisterValues) {
    setServerError('')
    const { data, error } = await apiClient.POST('/api/v1/auth/register', {
      body: { email: values.email, password: values.password, displayName: values.displayName },
    })

    if (error || !data?.data) {
      // The most common failure is a duplicate email (Identity rejects it at insert).
      setServerError('Registration failed. That email may already be registered.')
      return
    }

    // Register signs the new customer in (sets the auth cookies), so we go straight to
    // their account.
    signIn(data.data)
    navigate('/account', { replace: true })
  }

  return (
    <div className="mx-auto max-w-sm py-10">
      <Card>
        <CardHeader>
          <CardTitle className="text-xl">Create your account</CardTitle>
        </CardHeader>
        <CardContent>
          <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
            <div className="space-y-1">
              <label htmlFor="email" className="text-sm font-medium">
                Email
              </label>
              <Input id="email" type="email" autoComplete="email" {...register('email')} />
              {errors.email ? (
                <p className="text-sm text-destructive">{errors.email.message}</p>
              ) : null}
            </div>

            <div className="space-y-1">
              <label htmlFor="displayName" className="text-sm font-medium">
                Display name
              </label>
              <Input id="displayName" autoComplete="name" {...register('displayName')} />
              {errors.displayName ? (
                <p className="text-sm text-destructive">{errors.displayName.message}</p>
              ) : null}
            </div>

            <div className="space-y-1">
              <label htmlFor="password" className="text-sm font-medium">
                Password
              </label>
              <Input
                id="password"
                type="password"
                autoComplete="new-password"
                {...register('password')}
              />
              {errors.password ? (
                <p className="text-sm text-destructive">{errors.password.message}</p>
              ) : (
                <p className="text-xs text-muted-foreground">
                  At least 12 characters, with a letter and a number.
                </p>
              )}
            </div>

            {serverError ? <p className="text-sm text-destructive">{serverError}</p> : null}

            <Button type="submit" className="w-full" disabled={isSubmitting}>
              {isSubmitting ? 'Creating account…' : 'Create account'}
            </Button>
          </form>

          <p className="mt-4 text-center text-sm text-muted-foreground">
            Already have an account?{' '}
            <Link to="/login" className="text-primary hover:underline">
              Sign in
            </Link>
          </p>
        </CardContent>
      </Card>
    </div>
  )
}
