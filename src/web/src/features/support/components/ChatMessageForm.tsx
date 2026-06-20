import type { KeyboardEvent } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'

// Mirrors the server-side ChatWebhookRequestValidator (message 1..4000).
const schema = z.object({
  message: z
    .string()
    .trim()
    .min(1, 'Type a message.')
    .max(4000, 'Messages are limited to 4000 characters.'),
})

type ChatFormValues = z.infer<typeof schema>

interface ChatMessageFormProps {
  onSend: (message: string) => void
  disabled: boolean
}

/** The chat composer: a textarea + Send button. Enter sends; Shift+Enter inserts a newline. */
export function ChatMessageForm({ onSend, disabled }: ChatMessageFormProps) {
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<ChatFormValues>({
    resolver: zodResolver(schema),
    defaultValues: { message: '' },
  })

  function submit(values: ChatFormValues) {
    onSend(values.message)
    reset()
  }

  function onKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault()
      void handleSubmit(submit)()
    }
  }

  return (
    <form onSubmit={handleSubmit(submit)} className="shrink-0 border-t p-3">
      <Textarea
        {...register('message')}
        rows={2}
        placeholder="Ask about your orders, shipping…"
        aria-label="Message"
        aria-invalid={errors.message ? true : undefined}
        aria-describedby={errors.message ? 'chat-message-error' : undefined}
        disabled={disabled}
        onKeyDown={onKeyDown}
      />
      {errors.message ? (
        <p id="chat-message-error" role="alert" className="mt-1 text-xs text-destructive">
          {errors.message.message}
        </p>
      ) : null}
      <div className="mt-2 flex justify-end">
        <Button type="submit" size="sm" disabled={disabled}>
          {disabled ? 'Sending…' : 'Send'}
        </Button>
      </div>
    </form>
  )
}
