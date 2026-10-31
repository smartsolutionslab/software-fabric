import { defineConfig, devices } from '@playwright/test';

/** End-to-end tests against the running application: start the AppHost, then pnpm e2e. */
export default defineConfig({
  testDir: './e2e',
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:5173',
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
