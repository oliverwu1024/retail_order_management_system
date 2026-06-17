import type { Page } from '@playwright/test'

const CATEGORY_ID = '22222222-2222-2222-2222-222222222222'

export const PRODUCT_SUMMARY = {
  id: '11111111-1111-1111-1111-111111111111',
  sku: 'AERO-1',
  slug: 'aero-runner',
  name: 'Aero Runner',
  brandName: 'Velocity',
  categoryId: CATEGORY_ID,
  isPublished: true,
  primaryImageBlobKey: null,
  fromPriceCents: 12900,
}

export const PRODUCT_DETAIL = {
  ...PRODUCT_SUMMARY,
  description: 'A lightweight everyday running shoe.',
  variants: [
    {
      id: '33333333-3333-3333-3333-333333333333',
      sku: 'AERO-1-M',
      options: { size: 'M' },
      priceCents: 12900,
      compareAtPriceCents: null,
      isActive: true,
      onHand: 10,
      reserved: 0,
      available: 10,
      stockStatus: 'InStock',
    },
  ],
  images: [],
}

function envelope(status: number, data: unknown, ok: boolean) {
  return {
    status,
    contentType: 'application/json',
    body: JSON.stringify({
      success: ok,
      data,
      message: ok ? null : 'Not signed in.',
      errors: ok ? [] : [{ code: 'UNAUTHORIZED', message: 'Not signed in.', field: null }],
      traceId: null,
      timestamp: '2026-01-01T00:00:00Z',
    }),
  }
}

const jsonOk = (data: unknown) => envelope(200, data, true)
const unauthorized = () => envelope(401, null, false)

/**
 * Routes every /api/v1 call the storefront makes to a deterministic in-memory
 * response, so the E2E suite runs with NO backend (hermetic + CI-friendly).
 * One handler that switches on the path avoids Playwright route-ordering
 * surprises (later-registered routes are matched first).
 */
export async function mockStorefrontApi(page: Page) {
  await page.route('**/api/v1/**', (route) => {
    const path = new URL(route.request().url()).pathname
    if (path.endsWith('/auth/me')) return route.fulfill(unauthorized())
    if (path.endsWith('/catalog/categories')) return route.fulfill(jsonOk([]))
    if (path.includes('/catalog/products/')) return route.fulfill(jsonOk(PRODUCT_DETAIL))
    if (path.endsWith('/catalog/products')) {
      return route.fulfill(
        jsonOk({
          items: [PRODUCT_SUMMARY],
          page: 1,
          pageSize: 12,
          totalCount: 1,
          totalPages: 1,
          hasNext: false,
          hasPrevious: false,
        }),
      )
    }
    if (path.endsWith('/cart')) {
      return route.fulfill(jsonOk({ id: '00000000-0000-0000-0000-000000000000', items: [] }))
    }
    // csrf and anything else → a benign empty envelope.
    return route.fulfill(jsonOk(null))
  })
}
