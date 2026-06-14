import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { toast } from '@/hooks/use-toast'
import type { CustomerProfile } from '@/lib/api/types'
import { useUpdateProfile } from '../hooks/useAccountMutations'
import {
  profileFormSchema,
  toUpsertProfileBody,
  type ProfileFormValues,
} from '../lib/account-schema'

interface ProfileFormProps {
  profile: CustomerProfile
}

/**
 * Edits DisplayName + Phone. Email is shown read-only — it's the login identity and
 * immutable in the MVP, so it's rendered as a disabled field rather than a form input.
 */
export function ProfileForm({ profile }: ProfileFormProps) {
  const updateProfile = useUpdateProfile()

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<ProfileFormValues>({
    resolver: zodResolver(profileFormSchema),
    defaultValues: {
      displayName: profile.displayName ?? '',
      phone: profile.phone ?? '',
    },
  })

  function onSubmit(values: ProfileFormValues) {
    updateProfile.mutate(toUpsertProfileBody(values), {
      onSuccess: () => toast({ title: 'Profile saved' }),
      onError: () =>
        toast({
          variant: 'destructive',
          title: 'Couldn’t save profile',
          description: 'Please try again.',
        }),
    })
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
      <div className="space-y-1">
        <label htmlFor="email" className="text-sm font-medium">
          Email
        </label>
        <Input id="email" value={profile.email ?? ''} disabled />
        <p className="text-xs text-muted-foreground">Email is your login and can’t be changed.</p>
      </div>

      <div className="space-y-1">
        <label htmlFor="displayName" className="text-sm font-medium">
          Display name
        </label>
        <Input id="displayName" {...register('displayName')} />
        {errors.displayName ? (
          <p className="text-sm text-destructive">{errors.displayName.message}</p>
        ) : null}
      </div>

      <div className="space-y-1">
        <label htmlFor="phone" className="text-sm font-medium">
          Phone
        </label>
        <Input id="phone" type="tel" placeholder="optional" {...register('phone')} />
        {errors.phone ? <p className="text-sm text-destructive">{errors.phone.message}</p> : null}
      </div>

      <Button type="submit" disabled={updateProfile.isPending}>
        {updateProfile.isPending ? 'Saving…' : 'Save profile'}
      </Button>
    </form>
  )
}
