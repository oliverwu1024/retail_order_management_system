import { createBrowserRouter } from 'react-router-dom'
import { RoleGuard } from '@/app/guards/RoleGuard'
import { AdminHomePage } from '@/features/admin/AdminHomePage'
import { HomePage } from '@/features/storefront/HomePage'

// ─────────────────────────────────────────────────────────────────────────────
//  Application router (React Router v6.4+ data router).
//
//  WHY THE DATA ROUTER (createBrowserRouter) INSTEAD OF <BrowserRouter>?
//  --------------------------------------------------------------------
//  The data router unlocks loaders, actions, and deferred data. Even
//  though we use TanStack Query for most data fetching, route-level
//  loaders are still the right place for guards / auth checks / SSR-
//  friendly preloading. Starting on the data router keeps that option
//  open without a future migration.
//
//  Routes are intentionally flat at this stage. When admin sprouts a
//  shell layout (Phase 3), we'll add nested <Outlet /> children for the
//  sub-sections.
// ─────────────────────────────────────────────────────────────────────────────

export const router = createBrowserRouter([
  {
    path: '/',
    element: <HomePage />,
  },
  {
    path: '/admin',
    element: (
      <RoleGuard allowedRoles={['Admin', 'StoreManager']}>
        <AdminHomePage />
      </RoleGuard>
    ),
  },
])
