import { defineConfig, devices } from '@playwright/test';

/**
 * End-to-end tests (phase 8). Run through scripts/e2e.sh, which prepares a database and the API;
 * this file starts Vite against that API. Every test runs twice: on a phone and on a desktop.
 */
const webPort = Number(process.env.E2E_WEB_PORT ?? 5175);
const apiUrl = process.env.E2E_API_URL ?? 'http://localhost:5091';

export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  expect: { timeout: 10_000 },
  fullyParallel: true,
  workers: 2,
  reporter: [['list']],
  globalSetup: './e2e/global-setup.ts',
  use: {
    baseURL: `http://localhost:${webPort}`,
    // The machine's Chrome, so no browser has to be downloaded.
    channel: 'chrome',
    locale: 'fa-IR',
    timezoneId: 'Asia/Tehran',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'], channel: 'chrome', viewport: { width: 1366, height: 850 } } },
    { name: 'phone', use: { ...devices['Pixel 7'], channel: 'chrome' } },
  ],
  webServer: {
    command: `npx vite --port ${webPort} --strictPort`,
    port: webPort,
    reuseExistingServer: false,
    env: { VITE_API_BASE_URL: apiUrl },
  },
});
