import { defineConfig } from '@playwright/test';
export default defineConfig({ testDir: '.', timeout: 70_000, expect: { timeout: 60_000 }, retries: 0,
  workers: 1, reporter: [['line'], ['json', { outputFile: process.env.ZWEB_PLAYWRIGHT_JSON }]],
  use: { browserName: 'chromium', baseURL: 'https://localhost:8443', headless: true,
    ignoreHTTPSErrors: false, viewport: { width: 1280, height: 720 } } });
