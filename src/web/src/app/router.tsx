import { createBrowserRouter } from 'react-router-dom'
import { RoleGuard } from '@/app/guards/RoleGuard'
import { AdminShell } from '@/components/layouts/AdminShell'
import { StorefrontShell } from '@/components/layouts/StorefrontShell'
import { AccountPage } from '@/features/account/AccountPage'
import { AdminComingSoon } from '@/features/admin/AdminComingSoonPage'
import { AdminHomePage } from '@/features/admin/AdminHomePage'
import { AdminProductsPage } from '@/features/admin/AdminProductsPage'
import { ProductFormPage } from '@/features/admin/ProductFormPage'
import { UsersPage } from '@/features/admin/UsersPage'
import { LoginPage } from '@/features/auth/LoginPage'
import { RegisterPage } from '@/features/auth/RegisterPage'
import { CartPage } from '@/features/cart/CartPage'
import { CheckoutSuccessPage } from '@/features/checkout/CheckoutSuccessPage'
import { OrderDetailPage } from '@/features/orders/OrderDetailPage'
import { OrdersPage } from '@/features/orders/OrdersPage'
import { CatalogPage } from '@/features/storefront/CatalogPage'
import { ProductDetailPage } from '@/features/storefront/ProductDetailPage'
import { ADMIN_AREA_ROLES, ROLE_SETS } from '@/lib/auth/roleSets'

// ─────────────────────────────────────────────────────────────────────────────
//  Application router (React Router v7 data router).
//
//  TWO top-level layouts: the public storefront (<StorefrontShell /> header) and the
//  back-office (<AdminShell /> sidebar). They are SIBLINGS, not nested, so /admin gets
//  its own chrome rather than rendering inside the storefront header.
//
//  RBAC (Phase 3): the /admin area is gated to any back-office role; each child route
//  further-gates to its capability via ROLE_SETS (mirroring the backend policies), which
//  is also what drives the role-aware sidebar. Role names match the seeded roles
//  (Administrator / StoreManager / Staff / Customer) — NOT "Admin".
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
    ],
  },
  {
    // The admin back-office has its OWN shell (sidebar) — not the storefront header. The whole
    // area requires a back-office role; each child route further-gates to its capability.
    path: '/admin',
    element: (
      <RoleGuard allowedRoles={ADMIN_AREA_ROLES}>
        <AdminShell />
      </RoleGuard>
    ),
    children: [
      { index: true, element: <AdminHomePage /> },
      {
        path: 'orders',
        element: (
          <RoleGuard allowedRoles={ROLE_SETS.orders}>
            <AdminComingSoon
              title="Orders"
              note="The order workbench (list, fulfil, refund) lands in Chunk 2."
            />
          </RoleGuard>
        ),
      },
      // Catalog management is Administrator-only. React Router ranks the static "new"
      // segment above the ":id" param, so both resolve correctly.
      {
        path: 'products',
        element: (
          <RoleGuard allowedRoles={ROLE_SETS.catalog}>
            <AdminProductsPage />
          </RoleGuard>
        ),
      },
      {
        path: 'products/new',
        element: (
          <RoleGuard allowedRoles={ROLE_SETS.catalog}>
            <ProductFormPage />
          </RoleGuard>
        ),
      },
      {
        path: 'products/:id',
        element: (
          <RoleGuard allowedRoles={ROLE_SETS.catalog}>
            <ProductFormPage />
          </RoleGuard>
        ),
      },
      {
        path: 'inventory',
        element: (
          <RoleGuard allowedRoles={ROLE_SETS.inventory}>
            <AdminComingSoon title="Inventory" note="Stock adjustments land in Chunk 3." />
          </RoleGuard>
        ),
      },
      {
        path: 'audit',
        element: (
          <RoleGuard allowedRoles={ROLE_SETS.audit}>
            <AdminComingSoon title="Audit log" note="The audit-log viewer lands in Chunk 3." />
          </RoleGuard>
        ),
      },
      {
        path: 'reports',
        element: (
          <RoleGuard allowedRoles={ROLE_SETS.reports}>
            <AdminComingSoon title="Reports" note="Sales-by-day reporting lands in Chunk 3." />
          </RoleGuard>
        ),
      },
      {
        path: 'users',
        element: (
          <RoleGuard allowedRoles={ROLE_SETS.users}>
            <UsersPage />
          </RoleGuard>
        ),
      },
    ],
  },
])
