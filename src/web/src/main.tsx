import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider } from 'react-router-dom'
import { AppProviders } from '@/app/providers'
import { router } from '@/app/router'
import { Toaster } from '@/components/ui/toaster'
import { SessionBootstrapper } from '@/features/auth/SessionBootstrapper'
import '@/index.css'

// ─────────────────────────────────────────────────────────────────────────────
//  Root render — the order here matters.
//
//  Outermost is StrictMode: catches lifecycle bugs and unsafe patterns by
//  double-rendering in dev.
//  Then AppProviders (QueryClient + devtools): all data fetching downstream
//  has access to the cache.
//  Then RouterProvider: routes themselves can call useQuery from inside.
//  Toaster sits as a sibling so toasts persist across route changes (Toast
//  rendering is independent of which route is active).
// ─────────────────────────────────────────────────────────────────────────────

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AppProviders>
      <SessionBootstrapper>
        <RouterProvider router={router} />
      </SessionBootstrapper>
      <Toaster />
    </AppProviders>
  </StrictMode>,
)
