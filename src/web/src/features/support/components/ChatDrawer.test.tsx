import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { ChatDrawer } from './ChatDrawer'

// Mock the typed API client the send hook uses, so the drawer is tested without a backend.
const postMock = vi.fn()
vi.mock('@/lib/api/client', () => ({
  apiClient: { POST: (...args: unknown[]) => postMock(...args) },
}))

beforeAll(() => {
  // jsdom doesn't implement scrollIntoView (the drawer auto-scrolls on new messages).
  Element.prototype.scrollIntoView = vi.fn()
})

afterEach(() => postMock.mockReset())

function renderDrawer() {
  const client = new QueryClient({ defaultOptions: { mutations: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <ChatDrawer />
    </QueryClientProvider>,
  )
}

describe('ChatDrawer', () => {
  it('opens from the launcher and shows the greeting', async () => {
    renderDrawer()
    await userEvent.click(screen.getByRole('button', { name: /open support chat/i }))
    expect(screen.getByText(/i can help with your orders/i)).toBeInTheDocument()
  })

  it('shows the user message and the assistant reply', async () => {
    postMock.mockResolvedValue({
      data: { data: { reply: 'Your last order shipped yesterday.' } },
      error: undefined,
      response: { status: 200 },
    })

    renderDrawer()
    await userEvent.click(screen.getByRole('button', { name: /open support chat/i }))
    await userEvent.type(screen.getByLabelText('Message'), 'where is my order')
    await userEvent.click(screen.getByRole('button', { name: 'Send' }))

    expect(await screen.findByText('where is my order')).toBeInTheDocument()
    expect(await screen.findByText('Your last order shipped yesterday.')).toBeInTheDocument()

    // The turn posts to the webhook with the per-mount conversationId + the typed message.
    expect(postMock).toHaveBeenCalledWith('/api/v1/chat/webhook', {
      body: { conversationId: expect.any(String), message: 'where is my order' },
    })
  })

  it('renders an inline error bubble when the send fails', async () => {
    postMock.mockResolvedValue({
      data: undefined,
      error: { message: 'boom' },
      response: { status: 500 },
    })

    renderDrawer()
    await userEvent.click(screen.getByRole('button', { name: /open support chat/i }))
    await userEvent.type(screen.getByLabelText('Message'), 'hello')
    await userEvent.click(screen.getByRole('button', { name: 'Send' }))

    expect(await screen.findByText(/something went wrong/i)).toBeInTheDocument()
  })

  it('shows a sign-in prompt when the send is unauthorized (401)', async () => {
    postMock.mockResolvedValue({
      data: undefined,
      error: { message: 'unauthorized' },
      response: { status: 401 },
    })

    renderDrawer()
    await userEvent.click(screen.getByRole('button', { name: /open support chat/i }))
    await userEvent.type(screen.getByLabelText('Message'), 'hello')
    await userEvent.click(screen.getByRole('button', { name: 'Send' }))

    expect(await screen.findByText(/please sign in again/i)).toBeInTheDocument()
  })

  it('renders a friendly assistant turn (not an error) when the reply is null', async () => {
    // An AI outage comes back as a normal 200 with a null/blank reply — it must read as an assistant
    // bubble, never the error bubble.
    postMock.mockResolvedValue({
      data: { data: { reply: null } },
      error: undefined,
      response: { status: 200 },
    })

    renderDrawer()
    await userEvent.click(screen.getByRole('button', { name: /open support chat/i }))
    await userEvent.type(screen.getByLabelText('Message'), 'hello')
    await userEvent.click(screen.getByRole('button', { name: 'Send' }))

    expect(await screen.findByText(/didn't catch that/i)).toBeInTheDocument()
    expect(screen.queryByText(/something went wrong/i)).not.toBeInTheDocument()
  })

  const PROPOSAL = {
    reply: 'I can cancel that for you.',
    proposedAction: {
      type: 'confirm_return',
      orderId: 'order-1',
      orderNumber: 10012,
      refundAmountCents: 4200,
    },
  }

  async function openAndProposeRefund() {
    await userEvent.click(screen.getByRole('button', { name: /open support chat/i }))
    await userEvent.type(screen.getByLabelText('Message'), 'cancel order 10012')
    await userEvent.click(screen.getByRole('button', { name: 'Send' }))
  }

  it('renders a confirm card when a turn proposes a refund', async () => {
    postMock.mockResolvedValue({
      data: { data: PROPOSAL },
      error: undefined,
      response: { status: 200 },
    })

    renderDrawer()
    await openAndProposeRefund()

    expect(await screen.findByText(/cancel order #10012/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /confirm refund/i })).toBeInTheDocument()
  })

  it('cancels the order and confirms in chat when Confirm is clicked', async () => {
    postMock.mockImplementation((path: string) =>
      path === '/api/v1/chat/webhook'
        ? Promise.resolve({ data: { data: PROPOSAL }, error: undefined, response: { status: 200 } })
        : Promise.resolve({
            data: { data: { id: 'order-1' } },
            error: undefined,
            response: { status: 200 },
          }),
    )

    renderDrawer()
    await openAndProposeRefund()
    await userEvent.click(await screen.findByRole('button', { name: /confirm refund/i }))

    expect(await screen.findByText(/has been cancelled and a refund/i)).toBeInTheDocument()
    expect(postMock).toHaveBeenCalledWith('/api/v1/orders/{id}/cancel', {
      params: { path: { id: 'order-1' } },
    })
  })

  it('reports a failure (not a silent refund) when the cancel fails', async () => {
    postMock.mockImplementation((path: string) =>
      path === '/api/v1/chat/webhook'
        ? Promise.resolve({ data: { data: PROPOSAL }, error: undefined, response: { status: 200 } })
        : Promise.resolve({
            data: undefined,
            error: { message: 'boom' },
            response: { status: 500 },
          }),
    )

    renderDrawer()
    await openAndProposeRefund()
    await userEvent.click(await screen.findByRole('button', { name: /confirm refund/i }))

    expect(await screen.findByText(/process that cancellation/i)).toBeInTheDocument()
  })
})
