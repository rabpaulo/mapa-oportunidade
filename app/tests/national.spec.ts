import { expect, test } from '@playwright/test';
import { enterProject } from './helpers';

test('trocar UF limpa filtros, detalhes e conversa e muda mapas e informações da base', async ({ page, request }) => {
  const errors: string[] = [], queries: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('request', req => { if (req.url().includes('/api/')) queries.push(req.url()); });
  await page.route('https://tiles.openfreemap.org/**', route => route.abort());
  await page.route('**/api/chat?*', route => route.fulfill({ json: {
    texto: 'Conversa de teste no Ceará.', resultados: [], fontes: [], modelo: 'simulado'
  } }));
  await page.goto('/');
  await enterProject(page);
  const picker = page.getByLabel('Selecionar estado');
  await expect(picker).toHaveValue('CE');
  await page.getByRole('navigation').getByRole('button', { name: 'Assistente', exact: true }).click();
  await page.getByLabel('Sua pergunta').fill('Compare os municípios.');
  await page.getByRole('button', { name: 'Enviar pergunta', exact: true }).click();
  await expect(page.locator('.chat-message.model')).toContainText('Conversa de teste');
  await page.getByRole('navigation').getByRole('button', { name: 'Empresas', exact: true }).click();
  await page.getByLabel('Buscar empresas').fill('padaria');
  await expect(page.locator('.active-filters')).toContainText('padaria');
  await page.locator('.company-name').first().click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await picker.selectOption('SP');
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.getByLabel('Buscar empresas')).toHaveValue('');
  await expect(page.locator('.active-filters')).not.toContainText('padaria');
  await expect(page.locator('.contacts-table tbody tr').first()).toBeVisible();
  await expect(page.locator('.local-status')).toContainText('São Paulo');
  const info = await (await request.get('/api/base?uf=SP')).json();
  expect(info.uf).toBe('SP'); expect(info.perfil_dados).toBe('integral');
  await page.getByRole('navigation').getByRole('button', { name: 'Assistente', exact: true }).click();
  await expect(page.locator('.chat-message')).toHaveCount(0);
  await page.getByRole('navigation').getByRole('button', { name: 'Mapa', exact: true }).click();
  await expect(page.locator('.map-canvas canvas')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Enquadrar São Paulo', exact: true })).toBeVisible();
  await expect(page.locator('.municipality-panel')).toContainText('Municípios de São Paulo');
  await page.getByRole('navigation').getByRole('button', { name: 'Base', exact: true }).click();
  await expect(page.locator('.base-title')).toContainText('São Paulo');
  await expect(page.locator('.download-item')).toHaveCount(7);
  await picker.selectOption('CE');
  await expect(page.locator('.base-title')).toContainText('Ceará');
  expect(queries.some(url => url.includes('/api/malhas/malha_35.geojson?uf=SP'))).toBe(true);
  expect(queries.some(url => url.includes('/api/contatos/buscar?uf=SP'))).toBe(true);
  expect(errors).toEqual([]);
});
