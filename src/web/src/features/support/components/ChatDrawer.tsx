import { useEffect, useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Sheet } from '@/components/ui/sheet'
import { useCancelOrder } from '@/features/orders/hooks/useOrderMutations'
import type { ChatProposedAction } from '@/lib/api/types'
import { formatCents } from '@/lib/format'
import { cn } from '@/lib/utils'
import { ChatSendError, useSendChatMessage } from '../hooks/useSendChatMessage'
import { ChatMessageForm } from './ChatMessageForm'
import { ConfirmReturnCard } from './ConfirmReturnCard'

interface ChatBubble {
  id: string
  role: 'user' | 'assistant'
  content: string
}

const GREETING: ChatBubble = {
  id: 'greeting',
  role: 'assistant',
  content: 'Hi! I can help with your orders, shipping, and returns. What would you like to know?',
}

/**
 * The storefront support chatbot: a floating launcher that opens a right-side {@link Sheet} with a
 * message list + composer. One conversation per mount (a fresh `conversationId` GUID); messages live
 * in local state (history persistence is admin-only diagnostics). The backend returns a friendly 200
 * even on an AI outage, so most replies arrive as normal assistant turns; only auth / network errors
 * surface as an inline error bubble. When the assistant proposes a refund it arrives as a
 * `proposedAction`, rendered as a {@link ConfirmReturnCard} — nothing is cancelled until the customer
 * explicitly confirms, and the composer is locked while that cancel is in flight.
 */
export function ChatDrawer() {
  const [open, setOpen] = useState(false)
  const [messages, setMessages] = useState<ChatBubble[]>([GREETING])
  const [pendingAction, setPendingAction] = useState<ChatProposedAction | null>(null)
  // Stable per mount — the whole storefront session is one support conversation.
  const [conversationId] = useState(() => crypto.randomUUID())
  const send = useSendChatMessage()
  const cancel = useCancelOrder()

  const listEndRef = useRef<HTMLDivElement>(null)
  // Re-scroll when a message arrives, the drawer opens, the typing indicator toggles, or a card shows.
  useEffect(() => {
    listEndRef.current?.scrollIntoView({ block: 'end' })
  }, [messages, open, send.isPending, pendingAction])

  function appendAssistant(content: string) {
    setMessages((prev) => [...prev, { id: crypto.randomUUID(), role: 'assistant', content }])
  }

  function resolveAction(message: string) {
    appendAssistant(message)
    setPendingAction(null)
  }

  function handleSend(message: string) {
    setMessages((prev) => [...prev, { id: crypto.randomUUID(), role: 'user', content: message }])
    setPendingAction(null) // a new turn supersedes any prior proposal
    send.mutate(
      { conversationId, message },
      {
        onSuccess: (turn) => {
          appendAssistant(turn.reply ?? "Sorry, I didn't catch that — please try again.")
          setPendingAction(turn.proposedAction ?? null)
        },
        onError: (error) => {
          appendAssistant(
            error instanceof ChatSendError && error.status === 401
              ? 'Please sign in again to keep chatting.'
              : 'Sorry, something went wrong sending that. Please try again.',
          )
        },
      },
    )
  }

  function confirmReturn(action: ChatProposedAction) {
    if (!action.orderId) {
      resolveAction('Sorry, I couldn’t identify that order — please ask again.')
      return
    }
    cancel.mutate(action.orderId, {
      onSuccess: () =>
        resolveAction(
          `Done — order #${action.orderNumber} has been cancelled and a refund of ${formatCents(
            action.refundAmountCents ?? 0,
          )} is on its way.`,
        ),
      onError: () =>
        resolveAction(
          'Sorry, I couldn’t process that cancellation just now. Please try again in a moment.',
        ),
    })
  }

  return (
    <>
      <Button
        onClick={() => setOpen(true)}
        className="fixed bottom-6 right-6 z-40 rounded-full shadow-lg"
        aria-label="Open support chat"
      >
        Need help?
      </Button>

      <Sheet open={open} onOpenChange={setOpen} title="Support">
        <div className="flex-1 space-y-3 overflow-y-auto p-4" aria-live="polite">
          {messages.map((m) => (
            <div
              key={m.id}
              className={cn('flex', m.role === 'user' ? 'justify-end' : 'justify-start')}
            >
              <div
                className={cn(
                  'max-w-[80%] whitespace-pre-wrap rounded-lg px-3 py-2 text-sm',
                  m.role === 'user'
                    ? 'bg-primary text-primary-foreground'
                    : 'bg-muted text-foreground',
                )}
              >
                <span className="sr-only">
                  {m.role === 'user' ? 'You said: ' : 'Assistant said: '}
                </span>
                {m.content}
              </div>
            </div>
          ))}
          {pendingAction?.type === 'confirm_return' ? (
            <ConfirmReturnCard
              action={pendingAction}
              onConfirm={() => confirmReturn(pendingAction)}
              onDismiss={() => resolveAction('No problem — I’ll keep your order as is.')}
              isConfirming={cancel.isPending}
            />
          ) : null}
          {send.isPending ? (
            <div className="flex justify-start">
              <div className="rounded-lg bg-muted px-3 py-2 text-sm text-muted-foreground">…</div>
            </div>
          ) : null}
          <div ref={listEndRef} />
        </div>

        <ChatMessageForm onSend={handleSend} disabled={send.isPending || cancel.isPending} />
      </Sheet>
    </>
  )
}
