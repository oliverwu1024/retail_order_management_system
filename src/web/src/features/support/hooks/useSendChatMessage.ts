import { useMutation } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { ChatTurn } from '@/lib/api/types'

export interface SendChatInput {
  conversationId: string
  message: string
}

/** Error carrying the HTTP status so callers can tell a hard failure (401/422) from an LLM hiccup. */
export class ChatSendError extends Error {
  readonly status: number | undefined

  constructor(message: string, status: number | undefined) {
    super(message)
    this.name = 'ChatSendError'
    this.status = status
  }
}

/**
 * Posts one chat turn to the webhook and returns the assistant reply. CSRF + the JWT cookie are
 * attached automatically by {@link apiClient}. Note: an AI outage comes back as a normal 200 with a
 * friendly `reply` (the backend never 503s the chat), so most "failures" are not errors here — only
 * auth/validation/network/CSRF problems throw.
 */
export function useSendChatMessage() {
  return useMutation({
    mutationFn: async (input: SendChatInput): Promise<ChatTurn> => {
      const { data, error, response } = await apiClient.POST('/api/v1/chat/webhook', {
        body: { conversationId: input.conversationId, message: input.message },
      })
      if (error || !data?.data) {
        const serverMessage = (error as { message?: string } | undefined)?.message
        throw new ChatSendError(serverMessage ?? 'Could not send your message.', response?.status)
      }
      return data.data
    },
  })
}
