import { zodResolver } from '@hookform/resolvers/zod'
import { Controller, useForm } from 'react-hook-form'
import { z } from 'zod'
import { Button } from '@/components/ui/button'
import { RatingStars } from '@/components/ui/rating-stars'
import { Textarea } from '@/components/ui/textarea'
import { toast } from '@/hooks/use-toast'
import { useSubmitReview } from '../hooks/useReviewMutations'

// Mirrors the server-side SubmitReviewRequestValidator (rating 1..5, body 1..4000).
const reviewSchema = z.object({
  rating: z.number().int().min(1, 'Please choose a rating.').max(5),
  body: z
    .string()
    .trim()
    .min(1, 'Please write a few words.')
    .max(4000, 'Reviews are limited to 4000 characters.'),
})

type ReviewFormValues = z.infer<typeof reviewSchema>

export function ReviewSubmitForm({ productId }: { productId: string }) {
  const submit = useSubmitReview(productId)
  const {
    control,
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<ReviewFormValues>({
    resolver: zodResolver(reviewSchema),
    defaultValues: { rating: 0, body: '' },
  })

  function onSubmit(values: ReviewFormValues) {
    submit.mutate(values, {
      onSuccess: () => {
        toast({ title: 'Review submitted', description: 'Thanks for sharing your feedback!' })
        reset()
      },
      onError: (error) =>
        toast({
          variant: 'destructive',
          title: 'Could not submit your review',
          description: error instanceof Error ? error.message : 'Please try again.',
        }),
    })
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-3 rounded-lg border p-4">
      <p className="text-sm font-medium">Write a review</p>

      <div className="space-y-1">
        <Controller
          control={control}
          name="rating"
          render={({ field }) => (
            <RatingStars
              value={field.value}
              onChange={field.onChange}
              name="rating"
              label="Your rating"
            />
          )}
        />
        {errors.rating ? <p className="text-xs text-destructive">{errors.rating.message}</p> : null}
      </div>

      <div className="space-y-1">
        <Textarea
          {...register('body')}
          rows={4}
          placeholder="Share your experience with this product…"
          aria-label="Review"
        />
        {errors.body ? <p className="text-xs text-destructive">{errors.body.message}</p> : null}
      </div>

      <Button type="submit" disabled={submit.isPending}>
        {submit.isPending ? 'Submitting…' : 'Submit review'}
      </Button>
    </form>
  )
}
