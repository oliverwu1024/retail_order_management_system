import { useState, type ReactNode } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ReactQueryDevtools } from '@tanstack/react-query-devtools'

// ─────────────────────────────────────────────────────────────────────────────
//  <AppProviders /> — root-level provider stack.
//
//  Currently wraps:
//    - TanStack Query: server-state cache (queries, mutations, invalidation)
//    - React Query Devtools (dev-only via Vite's import.meta.env.DEV)
//
//  WHY useState(() => new QueryClient()) INSTEAD OF A MODULE-SCOPED CLIENT?
//  ----------------------------------------------------------------------
//  Module-scoped works in CSR but breaks under SSR — the same QueryClient
//  would be reused across requests, leaking one user's cache to another.
//  We're CSR-only today (Vite SPA), but using useState(...) costs nothing
//  and future-proofs against an SSR pivot.
//
//  defaultOptions HERE LOCK IN THE BASELINE BEHAVIOR FOR EVERY QUERY:
//    - staleTime 30s: queries are considered fresh for 30 seconds —
//      flips a tab and back without refetching every product list.
//    - retry: 1: one automatic retry on failure, then surface the error
//      to the UI. More retries hide flaky backends from us in dev.
// ─────────────────────────────────────────────────────────────────────────────

export function AppProviders({ children }: { children: ReactNode }) {
  const [queryClient] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            staleTime: 30_000,
            retry: 1,
            refetchOnWindowFocus: false,
          },
        },
      }),
  )

  return (
    <QueryClientProvider client={queryClient}>
      {children}
      {import.meta.env.DEV && <ReactQueryDevtools initialIsOpen={false} />}
    </QueryClientProvider>
  )
}
