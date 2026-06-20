import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { StorefrontShell } from './StorefrontShell'
import { useAuthStore } from '@/lib/store/auth-store'

// The shell renders useCartQuery (GET) and, for customers, the ChatDrawer (POST via the send hook).
vi.mock('@/lib/api/client', () => ({
  apiClient: {
    GET: vi.fn().mockResolvedValue({ data: undefined, error: undefined }),
    POST: vi.fn().mockResolvedValue({ data: undefined, error: undefined }),
  },
}))

beforeAll(() => {
  // jsdom doesn't implement scrollIntoView (the mounted ChatDrawer auto-scrolls).
  Element.prototype.scrollIntoView = vi.fn()
})

afterEach(() => {
  useAuthStore.setState({ user: null, isLoading: true })
})

function renderShell() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <StorefrontShell />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const launcher = () => screen.queryByRole('button', { name: /open support chat/i })

describe('StorefrontShell — chat launcher gating', () => {
  it('shows the launcher for a logged-in customer', () => {
    useAuthStore.setState({
      user: { id: 'u1', email: 'c@test.local', roles: ['Customer'] },
      isLoading: false,
    })
    renderShell()
    expect(launcher()).toBeInTheDocument()
  })

  it('hides the launcher for a guest', () => {
    useAuthStore.setState({ user: null, isLoading: false })
    renderShell()
    expect(launcher()).not.toBeInTheDocument()
  })

  it('hides the launcher for a back-office account', () => {
    useAuthStore.setState({
      user: { id: 'a1', email: 'admin@test.local', roles: ['Administrator'] },
      isLoading: false,
    })
    renderShell()
    expect(launcher()).not.toBeInTheDocument()
  })

  it('hides the launcher while auth is still loading', () => {
    useAuthStore.setState({
      user: { id: 'u1', email: 'c@test.local', roles: ['Customer'] },
      isLoading: true,
    })
    renderShell()
    expect(launcher()).not.toBeInTheDocument()
  })
})
