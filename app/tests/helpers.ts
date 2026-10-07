import { expect, type Page } from '@playwright/test';

export async function enterProject(page: Page) {
  await page.getByRole('button', { name: 'Explorar o mapa', exact: true }).click();
  await expect(page.locator('ceara-intro')).toHaveCount(0);
}
