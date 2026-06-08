import {
  Toast,
  ToastClose,
  ToastDescription,
  ToastProvider,
  ToastTitle,
  ToastViewport,
} from '@/components/ui/toast'
import { useToast } from '@/hooks/use-toast'

// ─────────────────────────────────────────────────────────────────────────────
//  <Toaster /> — the singleton mount point for the toast system.
//
//  Drop one <Toaster /> at the root of the app (next to RouterProvider).
//  Any component calling `toast({ title, description })` will then render
//  via this viewport. The hook + reducer in use-toast.ts handles state;
//  this component just renders.
// ─────────────────────────────────────────────────────────────────────────────

export function Toaster() {
  const { toasts } = useToast()

  return (
    <ToastProvider>
      {toasts.map(({ id, title, description, action, ...props }) => (
        <Toast key={id} {...props}>
          <div className="grid gap-1">
            {title && <ToastTitle>{title}</ToastTitle>}
            {description && <ToastDescription>{description}</ToastDescription>}
          </div>
          {action}
          <ToastClose />
        </Toast>
      ))}
      <ToastViewport />
    </ToastProvider>
  )
}
