import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'

// The chat launcher only shows for a logged-in customer, so this spec mocks /auth/me as a Customer
// (mock-api.ts returns it unauthorized). Hermetic: no backend, deterministic chat reply.
const CUSTOMER = { id: 'cust-1', email: 'demo@test.local', roles: ['Customer'] }

function envelope(data: unknown) {
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

async function mockChatApi(page: Page) {
  await page.route('**/api/v1/**', (route) => {
    const path = new URL(route.request().url()).pathname
    if (path.endsWith('/auth/me')) return route.fulfill(envelope(CUSTOMER))
    if (path.endsWith('/catalog/categories')) return route.fulfill(envelope([]))
    if (path.endsWith('/catalog/products')) {
      return route.fulfill(
        envelope({
          items: [],
          page: 1,
          pageSize: 12,
          totalCount: 0,
          totalPages: 0,
          hasNext: false,
          hasPrevious: false,
        }),
      )
    }
    if (path.endsWith('/cart')) {
      return route.fulfill(envelope({ id: '00000000-0000-0000-0000-000000000000', items: [] }))
    }
    if (path.endsWith('/chat/webhook')) {
      return route.fulfill(
        envelope({
          reply: 'Your most recent order is #10005 (Paid) for $42.00.',
          proposedAction: null,
        }),
      )
    }
    return route.fulfill(envelope(null))
  })
}

test.describe('support chat', () => {
  test.beforeEach(async ({ page }) => {
    await mockChatApi(page)
    // apiClient does CSRF fail-fast on state-changing requests (reads the non-httpOnly `csrf`
    // cookie) — seed it so the chat POST isn't rejected before it reaches the mock.
    await page.context().addCookies([{ name: 'csrf', value: 'e2e-csrf', url: 'http://localhost:5173' }])
  })

  test('a customer opens the drawer, sends a message, and gets a reply', async ({ page }) => {
    await page.goto('/')

    const launcher = page.getByRole('button', { name: /open support chat/i })
    await expect(launcher).toBeVisible()
    await launcher.click()

    const dialog = page.getByRole('dialog', { name: 'Support' })
    await expect(dialog).toBeVisible()

    await page.getByLabel('Message').fill('where are my orders?')
    await page.getByRole('button', { name: 'Send' }).click()

    await expect(page.getByText('where are my orders?')).toBeVisible()
    await expect(page.getByText(/most recent order is #10005/i)).toBeVisible()

    // a11y: no serious/critical violations with the drawer open (color-contrast is design-token-owned).
    const results = await new AxeBuilder({ page })
      .include('[role="dialog"]')
      .withTags(['wcag2a', 'wcag2aa'])
      .disableRules(['color-contrast'])
      .analyze()
    const serious = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical')
    expect(serious, serious.map((v) => `${v.id}: ${v.help}`).join('\n')).toEqual([])
  })
})
