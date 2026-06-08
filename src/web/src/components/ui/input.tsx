import * as React from 'react'
import { cn } from '@/lib/utils'

// ─────────────────────────────────────────────────────────────────────────────
//  <Input /> — shadcn-style input primitive.
//
//  Why so plain? It's the base. Forms layer on top with labels, error
//  states, and React Hook Form integration. Keeping this component
//  unopinionated about validation means it composes into checkout forms,
//  admin filters, and search bars without forking.
//
//  forwardRef is non-negotiable: React Hook Form's `register()` returns a
//  ref that needs to attach to the underlying <input>. Forgetting forwardRef
//  silently breaks form validation.
// ─────────────────────────────────────────────────────────────────────────────

const Input = React.forwardRef<HTMLInputElement, React.InputHTMLAttributes<HTMLInputElement>>(
  ({ className, type, ...props }, ref) => {
    return (
      <input
        type={type}
        className={cn(
          'flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background file:border-0 file:bg-transparent file:text-sm file:font-medium placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50',
          className,
        )}
        ref={ref}
        {...props}
      />
    )
  },
)
Input.displayName = 'Input'

export { Input }
