import AxeBuilder from '@axe-core/playwright'
import { expect, test } from '@playwright/test'
import { ADMIN_ORDER_ID, mockAdminApi } from './support/admin-mock'

test.describe('admin back-office', () => {
  test.beforeEach(async ({ page }) => {
    await mockAdminApi(page)
  })

  test('an administrator lands in the admin shell with role-gated nav', async ({ page }) => {
    await page.goto('/admin')
    // Scope to the sidebar nav so a dashboard CTA like "Manage products" isn't matched.
    const nav = page.getByRole('navigation')
    await expect(nav.getByRole('link', { name: 'Orders' })).toBeVisible()
    await expect(nav.getByRole('link', { name: 'Products' })).toBeVisible()
    await expect(nav.getByRole('link', { name: 'Users' })).toBeVisible()
  })

  test('create a product', async ({ page }) => {
    await page.goto('/admin/products/new')
    await expect(page.getByRole('heading', { name: 'New product' })).toBeVisible()

    await page.locator('#sku').fill('FORGE-X')
    await page.locator('#name').fill('Forge X')
    await page.locator('#categoryId').selectOption('cat-1')
    await page.getByRole('button', { name: 'Create product' }).click()

    // On success the page navigates to the new product's edit route (a persistent signal — the
    // success toast is transient, so we assert on the navigation instead).
    await expect(page).toHaveURL(/\/admin\/products\/new-prod-1$/)
  })

  test('mark a paid order shipped → Fulfilled', async ({ page }) => {
    await page.goto('/admin/orders')
    await page.getByRole('link', { name: '#10042' }).click()

    // Wait for the detail page to load (its heading) before asserting state, so the list page's
    // status filter/badge can't be matched.
    await expect(page.getByRole('heading', { name: 'Order #10042' })).toBeVisible()

    await page.getByRole('button', { name: 'Mark shipped' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.locator('#ship-carrier').fill('AusPost')
    await dialog.locator('#ship-tracking').fill('TRK-E2E')
    await dialog.getByRole('button', { name: 'Mark shipped' }).click()

    // The detail refetches and the status badge flips to Fulfilled (persistent), proving the ship
    // succeeded — more robust than the transient "Marked shipped" toast.
    await expect(page.getByText('Fulfilled')).toBeVisible()
  })

  test('the orders workbench has no serious/critical a11y violations', async ({ page }) => {
    await page.goto('/admin/orders')
    await expect(page.getByRole('link', { name: '#10042' })).toBeVisible()

    const results = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa'])
      .disableRules(['color-contrast'])
      .analyze()
    const serious = results.violations.filter(
      (v) => v.impact === 'serious' || v.impact === 'critical',
    )
    expect(serious, serious.map((v) => `${v.id}: ${v.help}`).join('\n')).toEqual([])
  })
})
