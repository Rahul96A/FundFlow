import { defineConfig, devices } from '@playwright/test'

/**
 * End-to-end tests drive the real UI against a running stack (API, SQL Server, RabbitMQ, Mailpit):
 *
 *   docker compose up -d --build            → E2E_BASE_URL=http://localhost:3000
 *   or: API on :5080 + `npm run dev`        → E2E_BASE_URL=http://localhost:5173  (default)
 *
 * Tests create their own organization and read emails from Mailpit, so they need no seeded credentials.
 *
 * E2E_BROWSER_CHANNEL=msedge (or chrome) drives an already-installed browser instead of Playwright's own Chromium
 * download, and E2E_MAILPIT_URL points the email helper at a non-default Mailpit.
 */
const channel = process.env.E2E_BROWSER_CHANNEL || undefined

export default defineConfig({
  testDir: './e2e',
  timeout: 90_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:5173',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'], channel }, grepInvert: /@mobile/ },
    { name: 'mobile', use: { ...devices['Pixel 7'], channel }, grep: /@mobile/ },
  ],
})
