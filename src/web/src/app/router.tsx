import { createBrowserRouter } from 'react-router-dom'
import { RoleGuard } from '@/app/guards/RoleGuard'
import { StorefrontShell } from '@/components/layouts/StorefrontShell'
import { AdminHomePage } from '@/features/admin/AdminHomePage'
import { CatalogPage } from '@/features/storefront/CatalogPage'
import { ProductDetailPage } from '@/features/storefront/ProductDetailPage'

// ─────────────────────────────────────────────────────────────────────────────
//  Application router (React Router v7 data router).
//
//  Storefront routes share a <StorefrontShell /> layout (header + <Outlet />).
//  Admin keeps its own RoleGuard branch and will grow a shell layout in Phase 3.
//  Role names match the backend's seeded roles (Administrator / StoreManager /
//  Staff / Customer) — NOT "Admin".
// ─────────────────────────────────────────────────────────────────────────────

export const router = createBrowserRouter([
  {
    element: <StorefrontShell />,
    children: [
      { path: '/', element: <CatalogPage /> },
      { path: '/products/:slug', element: <ProductDetailPage /> },
    ],
  },
  {
    path: '/admin',
    element: (
      <RoleGuard allowedRoles={['Administrator', 'StoreManager']}>
        <AdminHomePage />
      </RoleGuard>
    ),
  },
])
