import { createBrowserRouter } from 'react-router-dom'
import { RoleGuard } from '@/app/guards/RoleGuard'
import { StorefrontShell } from '@/components/layouts/StorefrontShell'
import { AdminHomePage } from '@/features/admin/AdminHomePage'
import { AdminProductsPage } from '@/features/admin/AdminProductsPage'
import { ProductFormPage } from '@/features/admin/ProductFormPage'
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
      // Product management is Administrator-only — the backend admin catalog
      // endpoints require that role, so the guard matches. React Router ranks
      // the static "new" segment above the ":id" param, so both resolve right.
      {
        path: '/admin/products',
        element: (
          <RoleGuard allowedRoles={['Administrator']}>
            <AdminProductsPage />
          </RoleGuard>
        ),
      },
      {
        path: '/admin/products/new',
        element: (
          <RoleGuard allowedRoles={['Administrator']}>
            <ProductFormPage />
          </RoleGuard>
        ),
      },
      {
        path: '/admin/products/:id',
        element: (
          <RoleGuard allowedRoles={['Administrator']}>
            <ProductFormPage />
          </RoleGuard>
        ),
      },
    ],
  },
])
