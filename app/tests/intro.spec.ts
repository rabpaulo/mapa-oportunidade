import { expect, test } from '@playwright/test';

test('apresentação preta revela o mapa com subida e transfere o foco', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Mapa de Oportunidades', exact: true })).toBeVisible();
  await expect(page.locator('.intro')).toHaveCSS('background-color', 'rgb(10, 10, 10)');
  await expect(page.locator('.app-shell')).toHaveCount(0);
  expect(await page.locator('.intro-geography img').evaluate((img: HTMLImageElement) => img.complete && img.naturalWidth > 0)).toBe(true);
  await page.getByRole('button', { name: 'Explorar o mapa', exact: true }).click();
  await expect(page.locator('.intro')).toHaveClass(/intro-leaving/);
  await expect(page.locator('.app-shell')).toHaveAttribute('inert', '');
  await expect(page.locator('ceara-intro')).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'Empresas por município' })).toBeVisible();
  await expect(page.locator('.map-canvas canvas')).toBeVisible();
  await expect(page.locator('#main')).toBeFocused();
  expect(errors).toEqual([]);
});

test('atalhos da apresentação entram na tela escolhida', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  for (const [button, heading] of [
    ['Consultar empresas', 'Empresas'], ['Empresas', 'Empresas'],
    ['Ramos', 'Ramos de atividade'], ['Base', 'Base'],
    ['Conheça os dados', 'Base'], ['Mapa', 'Empresas por município']
  ]) {
    await page.goto('/');
    await page.locator('ceara-intro').getByRole('button', { name: button, exact: true }).click();
    await expect(page.locator('ceara-intro')).toHaveCount(0);
    await expect(page.locator('.page-heading').getByRole('heading', { name: heading, exact: true })).toBeVisible();
    await expect(page.locator('#main')).toBeFocused();
  }
});

test('apresentação móvel permite entrar pelo teclado sem rolagem horizontal', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 740 });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('/');
  expect(await page.locator('.intro').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
  await page.setViewportSize({ width: 390, height: 844 });
  const cta = page.getByRole('button', { name: 'Explorar o mapa', exact: true });
  const bounds = await cta.boundingBox();
  expect(bounds!.y + bounds!.height).toBeLessThan(844);
  expect(await page.locator('.intro').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
  await page.screenshot({ path: 'test-results/abertura-mobile.png', fullPage: true });
  await cta.focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('ceara-intro')).toHaveCount(0);
  await expect(page.locator('.map-canvas canvas')).toBeVisible();
  await expect(page.locator('#main')).toBeFocused();
});
