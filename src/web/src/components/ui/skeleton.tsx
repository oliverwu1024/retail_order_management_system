import type { HTMLAttributes } from 'react'
import { cn } from '@/lib/utils'

/** Pulsing placeholder shown while data loads. */
export function Skeleton({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('animate-pulse rounded-md bg-muted', className)} {...props} />
}
