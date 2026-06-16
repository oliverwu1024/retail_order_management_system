import { createBrowserRouter } from 'react-router-dom'
import { RoleGuard } from '@/app/guards/RoleGuard'
import { StorefrontShell } from '@/components/layouts/StorefrontShell'
import { AccountPage } from '@/features/account/AccountPage'
import { AdminHomePage } from '@/features/admin/AdminHomePage'
import { AdminProductsPage } from '@/features/admin/AdminProductsPage'
import { ProductFormPage } from '@/features/admin/ProductFormPage'
import { LoginPage } from '@/features/auth/LoginPage'
import { RegisterPage } from '@/features/auth/RegisterPage'
import { CartPage } from '@/features/cart/CartPage'
import { CheckoutSuccessPage } from '@/features/checkout/CheckoutSuccessPage'
import { OrderDetailPage } from '@/features/orders/OrderDetailPage'
import { OrdersPage } from '@/features/orders/OrdersPage'
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
      // Cart is open to everyone — guests get an anonymous cart (anon_cart_key cookie).
      { path: '/cart', element: <CartPage /> },
      // Stripe success-return landing (open to all — guests check out too).
      { path: '/checkout/success', element: <CheckoutSuccessPage /> },
      // My Orders — Customer-only (the order endpoints require that role).
      {
        path: '/orders',
        element: (
          <RoleGuard allowedRoles={['Customer']}>
            <OrdersPage />
          </RoleGuard>
        ),
      },
      {
        path: '/orders/:id',
        element: (
          <RoleGuard allowedRoles={['Customer']}>
            <OrderDetailPage />
          </RoleGuard>
        ),
      },
      { path: '/login', element: <LoginPage /> },
      { path: '/register', element: <RegisterPage /> },
      // My Account is Customer-only — the /profile endpoints require that role.
      {
        path: '/account',
        element: (
          <RoleGuard allowedRoles={['Customer']}>
            <AccountPage />
          </RoleGuard>
        ),
      },
      {
        // Administrator-only: the backend admin catalog endpoints are Administrator-only,
        // so the whole /admin surface is scoped to match (no StoreManager dead-end until
        // the Phase 3 RBAC matrix gives StoreManager its own backed endpoints).
        path: '/admin',
        element: (
          <RoleGuard allowedRoles={['Administrator']}>
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
