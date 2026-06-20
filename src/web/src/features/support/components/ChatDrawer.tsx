import { useEffect, useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Sheet } from '@/components/ui/sheet'
import { cn } from '@/lib/utils'
import { ChatSendError, useSendChatMessage } from '../hooks/useSendChatMessage'
import { ChatMessageForm } from './ChatMessageForm'

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
 * in local state (history persistence is admin-only diagnostics, a later chunk). The backend returns
 * a friendly 200 even on an AI outage, so most replies arrive as normal assistant turns; only auth /
 * validation / network errors surface as an inline error bubble.
 */
export function ChatDrawer() {
  const [open, setOpen] = useState(false)
  const [messages, setMessages] = useState<ChatBubble[]>([GREETING])
  // Stable per mount — the whole storefront session is one support conversation. The useState lazy
  // initialiser computes the id ONCE; a bare useRef(crypto.randomUUID()) would re-evaluate the
  // expression on every render and discard the result.
  const [conversationId] = useState(() => crypto.randomUUID())
  const send = useSendChatMessage()

  const listEndRef = useRef<HTMLDivElement>(null)
  // Re-scroll when a message arrives, the drawer opens, OR the typing indicator toggles.
  useEffect(() => {
    listEndRef.current?.scrollIntoView({ block: 'end' })
  }, [messages, open, send.isPending])

  function handleSend(message: string) {
    setMessages((prev) => [...prev, { id: crypto.randomUUID(), role: 'user', content: message }])
    send.mutate(
      { conversationId, message },
      {
        onSuccess: (turn) =>
          setMessages((prev) => [
            ...prev,
            {
              id: crypto.randomUUID(),
              role: 'assistant',
              content: turn.reply ?? "Sorry, I didn't catch that — please try again.",
            },
          ]),
        onError: (error) => {
          const reply =
            error instanceof ChatSendError && error.status === 401
              ? 'Please sign in again to keep chatting.'
              : 'Sorry, something went wrong sending that. Please try again.'
          setMessages((prev) => [
            ...prev,
            { id: crypto.randomUUID(), role: 'assistant', content: reply },
          ])
        },
      },
    )
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
          {send.isPending ? (
            <div className="flex justify-start">
              <div className="rounded-lg bg-muted px-3 py-2 text-sm text-muted-foreground">…</div>
            </div>
          ) : null}
          <div ref={listEndRef} />
        </div>

        <ChatMessageForm onSend={handleSend} disabled={send.isPending} />
      </Sheet>
    </>
  )
}
