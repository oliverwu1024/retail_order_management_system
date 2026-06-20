import type { ReactNode } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { cn } from '@/lib/utils'

interface SheetProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description?: string
  children: ReactNode
  className?: string
}

/**
 * Accessible side drawer over Radix Dialog — same a11y stack as {@link Modal} (focus trap,
 * ESC-to-close, scroll lock, ARIA title/description) but anchored to the right edge, full height.
 * The body is a flex column that fills the remaining space, so content can put a scrolling region
 * above a pinned footer (e.g. a message list above a composer). Controlled via `open`/`onOpenChange`.
 */
export function Sheet({ open, onOpenChange, title, description, children, className }: SheetProps) {
  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-50 bg-black/40" />
        <Dialog.Content
          // When no <Dialog.Description> is rendered (e.g. the chat drawer passes no `description`),
          // tell Radix to drop its auto `aria-describedby` so it doesn't point at a non-existent id.
          {...(description ? {} : { 'aria-describedby': undefined })}
          className={cn(
            'fixed right-0 top-0 z-50 flex h-full w-full max-w-md flex-col border-l bg-background shadow-lg focus:outline-none',
            className,
          )}
        >
          <div className="flex items-center justify-between border-b px-4 py-3">
            <Dialog.Title className="text-base font-semibold">{title}</Dialog.Title>
            <Dialog.Close
              className="rounded-md p-1 text-muted-foreground hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              aria-label="Close"
            >
              ✕
            </Dialog.Close>
          </div>
          {description ? (
            <Dialog.Description className="px-4 pt-3 text-sm text-muted-foreground">
              {description}
            </Dialog.Description>
          ) : null}
          <div className="flex flex-1 flex-col overflow-hidden">{children}</div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
