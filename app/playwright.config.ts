import { defineConfig } from '@playwright/test';
export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  timeout: 60000,
  expect: { timeout: 15000 },
  use: {
    baseURL: process.env['CEARA_BASE_URL'] || 'http://127.0.0.1:3000',
    viewport: { width: 1440, height: 1000 },
    trace: 'retain-on-failure',
    launchOptions: { args: ['--enable-unsafe-swiftshader'], executablePath: process.env['CEARA_BROWSER_PATH'] },
  },
  reporter: 'list',
});
