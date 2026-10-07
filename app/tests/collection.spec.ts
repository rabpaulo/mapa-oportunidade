import { expect, test } from '@playwright/test';
import { enterProject } from './helpers';

test('coleta local seleciona UFs e mantém o acompanhamento entre abas', async ({ page }) => {
  let started = false, finished = false;
  let body: unknown;
  await page.route('**/api/coleta', route => {
    if (route.request().method() === 'POST') { body = route.request().postDataJSON(); started = true; }
    return route.fulfill({ status: route.request().method() === 'POST' ? 202 : 200, json: {
      status: started ? finished ? 'concluido' : 'rodando' : 'ocioso',
      ufs: started ? ['AC', 'SP'] : [], linhas: started ? ['Preparando AC, SP'] : []
    } });
  });
  await page.goto('/'); await enterProject(page);
  await page.getByRole('navigation').getByRole('button', { name: 'Base', exact: true }).click();
  await expect(page.locator('.state-selection input')).toHaveCount(27);
  await page.locator('.state-selection label').filter({ hasText: 'AC · Acre' }).getByRole('checkbox').check();
  await page.locator('.state-selection label').filter({ hasText: 'SP · São Paulo' }).getByRole('checkbox').check();
  await page.getByLabel('Reaproveitar os recortes já baixados').check();
  await page.getByRole('button', { name: 'Baixar 2 estado(s)', exact: true }).click();
  expect(body).toEqual({ ufs: ['AC', 'SP'], reaproveitar: true });
  await expect(page.getByRole('button', { name: 'Coleta em andamento…' })).toBeDisabled();
  await page.getByRole('navigation').getByRole('button', { name: 'Empresas', exact: true }).click();
  await expect(page.locator('.contacts-table tbody tr').first()).toBeVisible();
  finished = true;
  await page.getByRole('navigation').getByRole('button', { name: 'Base', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('Coleta concluída');
  await expect(page.getByLabel('Log da coleta')).toContainText('Preparando AC, SP');
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: 'test-results/coleta-local-mobile.png', fullPage: true });
});

test('sem chave própria o usuário pode coletar dados e recebe instruções do Gemini', async ({ page }) => {
  await page.route('**/api/base?*', async route => {
    const response = await route.fetch(); await route.fulfill({ json: { ...await response.json(), ia_configurada: false } });
  });
  await page.goto('/'); await enterProject(page);
  await page.getByRole('navigation').getByRole('button', { name: 'Assistente', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('Configure sua própria GEMINI_API_KEY');
  await page.getByLabel('Sua pergunta').fill('Olá');
  await expect(page.getByRole('button', { name: 'Enviar pergunta', exact: true })).toBeDisabled();
  await page.getByRole('navigation').getByRole('button', { name: 'Base', exact: true }).click();
  await expect(page.locator('.state-selection input')).toHaveCount(27);
});
