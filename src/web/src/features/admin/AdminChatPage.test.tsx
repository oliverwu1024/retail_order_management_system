import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AdminChatPage } from './AdminChatPage'

// Mock the two react-query hooks so the page renders without a backend.
const listMock = vi.fn()
const detailMock = vi.fn()
vi.mock('./hooks/useChatSessions', () => ({
  useChatSessionsQuery: () => listMock(),
  useChatSessionQuery: () => detailMock(),
}))

afterEach(() => {
  listMock.mockReset()
  detailMock.mockReset()
})

const PAGE = {
  items: [
    {
      id: 's1',
      conversationId: 'c1',
      customerProfileId: 'p1',
      startedAt: '2026-06-20T00:00:00Z',
      lastMessageAt: '2026-06-20T00:01:00Z',
      messageCount: 3,
    },
  ],
  page: 1,
  totalPages: 1,
  hasPrevious: false,
  hasNext: false,
}

describe('AdminChatPage', () => {
  it('lists sessions and opens a transcript', async () => {
    listMock.mockReturnValue({ data: PAGE, isLoading: false, isError: false })
    detailMock.mockReturnValue({
      data: {
        id: 's1',
        conversationId: 'c1',
        startedAt: '2026-06-20T00:00:00Z',
        lastMessageAt: '2026-06-20T00:01:00Z',
        messages: [
          { role: 'User', content: 'where is my order', createdAt: '2026-06-20T00:00:00Z' },
        ],
      },
      isLoading: false,
      isError: false,
    })

    render(<AdminChatPage />)
    expect(screen.getByText('3')).toBeInTheDocument() // message count

    await userEvent.click(screen.getByRole('button', { name: 'View' }))
    expect(await screen.findByText('where is my order')).toBeInTheDocument()
  })

  it('shows the empty state when there are no sessions', () => {
    listMock.mockReturnValue({
      data: { items: [], page: 1, totalPages: 1, hasPrevious: false, hasNext: false },
      isLoading: false,
      isError: false,
    })
    detailMock.mockReturnValue({ data: undefined, isLoading: false, isError: false })

    render(<AdminChatPage />)
    expect(screen.getByText(/no chat sessions/i)).toBeInTheDocument()
  })
})
