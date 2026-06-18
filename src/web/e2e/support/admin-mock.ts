import type { Page } from '@playwright/test'

export const ADMIN_ORDER_ID = '44444444-4444-4444-4444-444444444444'

const ADMIN_USER = {
  id: 'admin-1',
  email: 'admin@retail.local',
  displayName: 'Admin',
  roles: ['Administrator'],
}
const CATEGORY = { id: 'cat-1', name: 'Footwear', slug: 'footwear' }

const EMPTY_PAGE = {
  items: [],
  page: 1,
  pageSize: 20,
  totalCount: 0,
  totalPages: 0,
  hasNext: false,
  hasPrevious: false,
}

// The same order before/after fulfilment — the detail route returns the shipped variant once the
// ship POST has flipped the in-memory flag, so the UI reflects Paid → Fulfilled.
function order(shipped: boolean) {
  return {
    id: ADMIN_ORDER_ID,
    orderNumber: 10042,
    status: shipped ? 'Fulfilled' : 'Paid',
    customerEmail: 'buyer@example.com',
    placedAt: '2026-01-01T10:00:00Z',
    subtotalCents: 12900,
    taxCents: 1290,
    shippingCents: 0,
    totalCents: 14190,
    lines: [{ productName: 'Aero Runner', sku: 'AERO-1-M', quantity: 1, lineTotalCents: 12900 }],
    payments: [{ amountCents: 14190, status: 'Succeeded', createdAt: '2026-01-01T10:01:00Z' }],
    shipment: shipped
      ? {
          status: 'Shipped',
          carrier: 'AusPost',
          trackingNumber: 'TRK-E2E',
          shippedAt: '2026-01-02T00:00:00Z',
          deliveredAt: null,
        }
      : null,
    shippingAddress: null,
  }
}

const ORDER_SUMMARY = {
  items: [
    {
      id: ADMIN_ORDER_ID,
      orderNumber: 10042,
      status: 'Paid',
      customerEmail: 'buyer@example.com',
      placedAt: '2026-01-01T10:00:00Z',
      totalCents: 14190,
    },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 1,
  totalPages: 1,
  hasNext: false,
  hasPrevious: false,
}

function ok(data: unknown) {
  return {
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      success: true,
      data,
      message: null,
      errors: [],
      traceId: null,
      timestamp: '2026-01-01T00:00:00Z',
    }),
  }
}

/**
 * Hermetic admin back-office API: authenticates as an Administrator and stubs the catalog + order
 * endpoints the admin flows touch. One stateful handler (the `shipped` flag) so the ship POST is
 * reflected by the next order-detail GET.
 */
export async function mockAdminApi(page: Page) {
  // The apiClient does CSRF fail-fast on state-changing requests: it reads the (non-httpOnly) `csrf`
  // cookie and echoes it as a header, throwing if absent. Seed one so POSTs (create/ship) go out.
  await page
    .context()
    .addCookies([{ name: 'csrf', value: 'e2e-csrf', url: 'http://localhost:5173' }])

  let shipped = false
  await page.route('**/api/v1/**', (route) => {
    const path = new URL(route.request().url()).pathname
    const method = route.request().method()

    if (path.endsWith('/auth/me')) return route.fulfill(ok(ADMIN_USER))
    if (path.endsWith('/catalog/categories')) return route.fulfill(ok([CATEGORY]))
    if (path.endsWith('/catalog/products') && method === 'POST') {
      return route.fulfill(
        ok({
          id: 'new-prod-1',
          slug: 'forge-x',
          name: 'Forge X',
          sku: 'FORGE-X',
          isPublished: false,
        }),
      )
    }
    if (path.includes('/catalog/admin/products/')) {
      return route.fulfill(
        ok({
          id: 'new-prod-1',
          slug: 'forge-x',
          name: 'Forge X',
          category: CATEGORY,
          variants: [],
          images: [],
        }),
      )
    }
    if (path.endsWith('/catalog/admin/products')) return route.fulfill(ok(EMPTY_PAGE))
    if (path.endsWith(`/admin/orders/${ADMIN_ORDER_ID}/ship`) && method === 'POST') {
      shipped = true
      return route.fulfill(ok(order(true)))
    }
    if (path.endsWith(`/admin/orders/${ADMIN_ORDER_ID}`)) return route.fulfill(ok(order(shipped)))
    if (path.endsWith('/admin/orders')) return route.fulfill(ok(ORDER_SUMMARY))
    if (path.endsWith('/admin/users')) return route.fulfill(ok(EMPTY_PAGE))
    return route.fulfill(ok(null)) // csrf and anything else
  })
}
