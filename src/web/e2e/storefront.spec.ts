import { expect, test } from '@playwright/test'
import { mockStorefrontApi } from './support/mock-api'

test.describe('storefront golden path', () => {
  test.beforeEach(async ({ page }) => {
    await mockStorefrontApi(page)
  })

  test('browse the catalog and open a product', async ({ page }) => {
    await page.goto('/')

    // The layout shell renders (brand link in the header).
    await expect(page.getByRole('link', { name: 'Retail OMS' })).toBeVisible()

    // The mocked product appears on the grid …
    const productLink = page.getByRole('link', { name: /Aero Runner/i })
    await expect(productLink).toBeVisible()

    // … and clicking it routes to the product detail page.
    await productLink.click()
    await expect(page).toHaveURL(/\/products\/aero-runner$/)
    await expect(page.getByRole('heading', { name: 'Aero Runner' })).toBeVisible()
  })
})
