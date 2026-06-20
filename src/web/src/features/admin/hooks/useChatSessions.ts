import { useQuery } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import type { ChatSessionDetail, ChatSessionPage } from '@/lib/api/types'

export const chatSessionKeys = {
  all: ['admin', 'chat'] as const,
  list: (page: number, pageSize: number) => ['admin', 'chat', 'list', page, pageSize] as const,
  detail: (id: string) => ['admin', 'chat', 'detail', id] as const,
}

/** Lists chat sessions (Chat.View — StoreManager + Administrator), most recently active first. */
export function useChatSessionsQuery(page: number, pageSize: number) {
  return useQuery({
    queryKey: chatSessionKeys.list(page, pageSize),
    queryFn: async (): Promise<ChatSessionPage> => {
      const { data, error } = await apiClient.GET('/api/v1/chat/sessions', {
        params: { query: { Page: page, PageSize: pageSize } },
      })
      if (error || !data?.data) {
        throw new Error('Failed to load chat sessions.')
      }
      return data.data
    },
  })
}

/** Loads one chat session's full message history (gated on a selected id). */
export function useChatSessionQuery(id: string | null) {
  return useQuery({
    queryKey: chatSessionKeys.detail(id ?? ''),
    enabled: id !== null,
    queryFn: async (): Promise<ChatSessionDetail> => {
      const { data, error } = await apiClient.GET('/api/v1/chat/sessions/{id}', {
        params: { path: { id: id! } },
      })
      if (error || !data?.data) {
        throw new Error('Failed to load the chat session.')
      }
      return data.data
    },
  })
}
