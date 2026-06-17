import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'
import { mockStorefrontApi } from './support/mock-api'

// Fails the test on any serious/critical WCAG 2 A/AA violation. Color-contrast
// is owned by the design system (shadcn/Tailwind tokens) and tuned separately,
// so it's excluded here to keep this gate about structure/labels/roles.
async function seriousViolations(page: Page) {
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa'])
    .disableRules(['color-contrast'])
    .analyze()
  return results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical')
}

function describe(violations: Awaited<ReturnType<typeof seriousViolations>>) {
  return violations.map((v) => `${v.id} (${v.impact}): ${v.help}`).join('\n')
}

test.describe('accessibility', () => {
  test.beforeEach(async ({ page }) => {
    await mockStorefrontApi(page)
  })

  test('catalog home has no serious/critical violations', async ({ page }) => {
    await page.goto('/')
    await expect(page.getByRole('link', { name: /Aero Runner/i })).toBeVisible()
    const violations = await seriousViolations(page)
    expect(violations, describe(violations)).toEqual([])
  })

  test('login page has no serious/critical violations', async ({ page }) => {
    await page.goto('/login')
    await expect(page.getByRole('button', { name: /sign in|log in/i })).toBeVisible()
    const violations = await seriousViolations(page)
    expect(violations, describe(violations)).toEqual([])
  })
})
