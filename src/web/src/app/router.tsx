import { createBrowserRouter } from 'react-router-dom'
import { RoleGuard } from '@/app/guards/RoleGuard'
import { StorefrontShell } from '@/components/layouts/StorefrontShell'
import { AdminHomePage } from '@/features/admin/AdminHomePage'
import { LoginPage } from '@/features/auth/LoginPage'
import { CatalogPage } from '@/features/storefront/CatalogPage'
import { ProductDetailPage } from '@/features/storefront/ProductDetailPage'

// ─────────────────────────────────────────────────────────────────────────────
//  Application router (React Router v7 data router).
//
//  Everything shares the <StorefrontShell /> layout (header + <Outlet />) for now;
//  Phase 3 will give /admin its own AdminShell. Role names match the backend's
//  seeded roles (Administrator / StoreManager / Staff / Customer) — NOT "Admin".
// ─────────────────────────────────────────────────────────────────────────────

export const router = createBrowserRouter([
  {
    element: <StorefrontShell />,
    children: [
      { path: '/', element: <CatalogPage /> },
      { path: '/products/:slug', element: <ProductDetailPage /> },
      { path: '/login', element: <LoginPage /> },
      {
        path: '/admin',
        element: (
          <RoleGuard allowedRoles={['Administrator', 'StoreManager']}>
            <AdminHomePage />
          </RoleGuard>
        ),
      },
    ],
  },
])
