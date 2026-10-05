import { expect, test } from '@playwright/test';

test('mapa real, seleção de município e tema escuro', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', e => errors.push(e.message));
  await page.goto('/');
  await expect(page.getByText('680.298', { exact: true })).toBeVisible();
  await expect(page.locator('.map-canvas canvas')).toBeVisible();
  expect((await page.locator('.map-canvas').boundingBox())?.height).toBeGreaterThan(400);
  await expect(page.locator('.map-status')).toHaveCount(0);
  await page.locator('.municipality-list button').filter({ hasText: 'Fortaleza' }).click();
  await expect(page.getByRole('heading', { name: 'Fortaleza', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Explorar empresas', exact: true }).click();
  await expect(page.locator('.contacts-table tbody tr')).toHaveCount(50);
  await expect(page.getByRole('combobox').filter({ has: page.locator('option[value="Fortaleza"]') }).first()).toHaveValue('Fortaleza');
  await page.getByRole('button', { name: 'Ativar tema escuro' }).click();
  await expect(page.locator('html')).toHaveAttribute('data-tema', 'escuro');
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-tema', 'escuro');
  expect(errors).toEqual([]);
});

test('filtros, detalhes, seleção, paginação e download Excel', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('navigation').getByRole('button', { name: 'Empresas', exact: true }).click();
  await expect(page.locator('.contacts-table tbody tr')).toHaveCount(50);
  await page.getByLabel('Buscar empresas').fill('padaria');
  await expect(page.locator('.active-filters')).toContainText('padaria');
  await expect(page.locator('.contacts-table tbody tr')).toHaveCount(50);
  await page.locator('.company-name').first().click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await expect(page.getByRole('dialog')).toContainText('CNPJ');
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await page.locator('.contacts-table tbody input[type="checkbox"]').first().check();
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Exportar 1', exact: true }).click();
  expect((await download).suggestedFilename()).toBe('oportunidades-ceara.xlsx');
  await page.getByRole('button', { name: 'Próxima página', exact: true }).click();
  await expect(page.locator('.pagination')).toContainText('Página 2');
  await expect(page.getByRole('button', { name: 'Exportar 1', exact: true })).toBeVisible();
});

test('ramos abrem um recorte e a manutenção mostra a origem', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('navigation').getByRole('button', { name: 'Ramos', exact: true }).click();
  await page.getByLabel('Buscar ramo').fill('padaria');
  await expect(page.locator('.segments-table tbody tr')).toHaveCount(1);
  await page.locator('.segments-table .company-name').click();
  await expect(page.locator('.active-filters')).toContainText('Padaria e confeitaria');
  await expect(page.locator('.contacts-table tbody tr')).toHaveCount(50);
  await page.getByRole('navigation').getByRole('button', { name: 'Base', exact: true }).click();
  await expect(page.locator('.base-facts')).toContainText('680.298');
  await expect(page.locator('.base-facts')).toContainText('14/09/2026');
  await expect(page.getByRole('button', { name: 'Regenerar com dados locais', exact: true })).toBeVisible();
});

test('assistente aceita conversa geral, preserva a sessão e mostra análises', async ({ page }) => {
  const requests: unknown[] = [];
  await page.route('**/api/chat', async route => {
    const body = route.request().postDataJSON(); requests.push(body);
    await route.fulfill({ json: { texto: 'Podemos organizar uma pesquisa de mercado. Fortaleza tem 316.136 empresas nesta base.',
      resultados: [{ tipo: 'analise', titulo: 'Comparação', filtros: { cidade: '', cidades: [], segmento: '', segmentos: [], termo: '', porte: '', bairro: '', ano_minimo: null, ano_maximo: null, score_minimo: 0, somente_celular: false, somente_email: false, somente_sem_dominio: false, ordem: 'score' }, agrupar_por: ['cidade'], itens: [{ cidade: 'Fortaleza', contatos: 316136, com_celular: 0, com_email: 0, score_medio: 64 }] }],
      fontes: [{ fonte: 'Receita Federal / Garimpo — recorte CE', versao: '2026-09-14', filtros: { cidade: '', cidades: [], segmento: '', segmentos: [], termo: '', porte: '', bairro: '', ano_minimo: null, ano_maximo: null, score_minimo: 0, somente_celular: false, somente_email: false, somente_sem_dominio: false, ordem: 'score' } }], modelo: 'gemini-simulado' } });
  });
  await page.goto('/');
  await page.getByRole('navigation').getByRole('button', { name: 'Assistente', exact: true }).click();
  await page.getByLabel('Sua pergunta').fill('Me ajude a planejar uma pesquisa.');
  await page.getByRole('button', { name: 'Enviar pergunta', exact: true }).click();
  await expect(page.locator('.chat-message.model')).toContainText('pesquisa de mercado');
  await expect(page.locator('.chat-result')).toContainText('316.136');
  await expect(page.locator('.chat-sources')).toContainText('2026-09-14');
  await page.getByRole('navigation').getByRole('button', { name: 'Mapa', exact: true }).click();
  await page.getByRole('navigation').getByRole('button', { name: 'Assistente', exact: true }).click();
  await expect(page.locator('.chat-message.model')).toHaveCount(1);
  await page.getByLabel('Sua pergunta').fill('E um exemplo?');
  await page.getByRole('button', { name: 'Enviar pergunta', exact: true }).click();
  await expect(page.locator('.chat-message.model')).toHaveCount(2);
  expect((requests[1] as { historico: unknown[] }).historico).toHaveLength(2);
  await page.getByRole('button', { name: 'Nova conversa', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'O que você quer descobrir?' })).toBeVisible();
});

test('erro de quota da IA é claro e empresas continuam disponíveis', async ({ page }) => {
  await page.route('**/api/chat', route => route.fulfill({ status: 429, json: { detail: 'A cota gratuita do Gemini foi atingida. Aguarde e tente novamente.' } }));
  await page.goto('/');
  await page.getByRole('navigation').getByRole('button', { name: 'Assistente', exact: true }).click();
  await page.getByLabel('Sua pergunta').fill('Olá');
  await page.getByRole('button', { name: 'Enviar pergunta', exact: true }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'cota gratuita' })).toBeVisible();
  await page.getByRole('navigation').getByRole('button', { name: 'Empresas', exact: true }).click();
  await expect(page.locator('.contacts-table tbody tr')).toHaveCount(50);
});

test('layout móvel, filtros recolhidos e captura das telas', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('.map-canvas canvas')).toBeVisible();
  await expect(page.locator('.map-status')).toHaveCount(0);
  await page.screenshot({ path: 'test-results/mapa-desktop.png', fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.reload();
  await expect(page.getByText('680.298', { exact: true })).toBeVisible();
  await expect(page.locator('.map-canvas canvas')).toBeVisible();
  await expect(page.locator('.map-status')).toHaveCount(0);
  await page.screenshot({ path: 'test-results/mapa-mobile.png', fullPage: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
  await page.getByRole('navigation').getByRole('button', { name: 'Empresas', exact: true }).click();
  await expect(page.locator('.contacts-table tbody tr')).toHaveCount(50);
  await expect(page.locator('.filters-panel')).not.toBeVisible();
  await page.getByRole('button', { name: 'Filtros', exact: true }).click();
  await expect(page.locator('.filters-panel')).toBeVisible();
  await page.getByRole('button', { name: 'Filtros', exact: true }).click();
  await expect(page.locator('.filters-panel')).not.toBeVisible();
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
  await page.screenshot({ path: 'test-results/empresas-mobile.png', fullPage: true });
});
