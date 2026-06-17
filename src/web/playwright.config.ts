import { defineConfig, devices } from '@playwright/test'

const isCI = !!process.env.CI

/**
 * Playwright E2E config. The suite is hermetic — every test stubs the /api/v1
 * layer via page.route (see e2e/support/mock-api.ts), so it runs with NO backend
 * and is deterministic in CI. The web server is the Vite dev server; route
 * interception happens in the browser, so the dev proxy to the API is never hit.
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: isCI,
  retries: isCI ? 1 : 0,
  workers: isCI ? 1 : undefined,
  reporter: isCI ? [['github'], ['html', { open: 'never' }]] : [['list']],
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: 'pnpm dev --port 5173',
    url: 'http://localhost:5173',
    reuseExistingServer: !isCI,
    timeout: 120_000,
  },
})
