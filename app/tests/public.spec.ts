import { expect, test } from '@playwright/test';
import { enterProject } from './helpers';

test('publicação minimizada oferece empresas do Ceará sem campos pessoais', async ({ page, request }) => {
  const errors: string[] = [], external: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('request', req => { if (req.url().startsWith('http') && new URL(req.url()).origin !== new URL(process.env['CEARA_BASE_URL'] || 'http://127.0.0.1:3000').origin) external.push(req.url()); });
  const info = await (await request.get('/api/base')).json();
  expect(info.uf).toBe('CE'); expect(info.perfil_dados).toBe('minimizado'); expect(info.publicacao_restrita).toBe(true);
  await page.goto('/');
  await enterProject(page);
  await expect(page.locator('.map-canvas canvas')).toBeVisible();
  await expect(page.getByLabel('Selecionar estado')).toHaveCount(0);
  await expect(page.getByRole('navigation').getByRole('button', { name: 'Assistente', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Mapa de ruas', exact: true })).toHaveCount(0);
  await page.getByRole('navigation').getByRole('button', { name: 'Empresas', exact: true }).click();
  await expect(page.locator('.contacts-table tbody tr')).toHaveCount(50);
  await expect(page.getByLabel('Bairro')).toHaveCount(0);
  await expect(page.locator('.contacts-table thead')).not.toContainText('Contato');
  await expect(page.locator('.company-name').first()).toHaveText(/^[A-Z0-9]{14}$/);
  await page.locator('.company-name').first().click();
  const dialog = page.getByRole('dialog'); await expect(dialog).toBeVisible();
  await expect(dialog).toContainText('CNPJ'); await expect(dialog).not.toContainText('E-mail'); await expect(dialog).not.toContainText('Endereço');
  await dialog.getByRole('button', { name: 'Fechar detalhes' }).click();
  await page.getByLabel('Buscar empresas').fill('padaria');
  await expect(page.locator('.active-filters')).toContainText('padaria');
  await expect(page.locator('.contacts-table tbody tr').first()).toBeVisible();
  const exportEvent = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Baixar recorte CSV', exact: true }).click();
  const exportFile = await exportEvent; expect(exportFile.suggestedFilename()).toBe('ce-recorte.csv.gz'); expect(await exportFile.failure()).toBeNull();
  await page.getByRole('navigation').getByRole('button', { name: 'Base', exact: true }).click();
  await expect(page.locator('.download-item')).toHaveCount(0);
  await expect(page.locator('.state-selection')).toHaveCount(0);
  await expect(page.locator('.base-layout')).toContainText('Visitantes não precisam fornecer uma chave');
  await page.screenshot({ path: 'test-results/base-publica.png', fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
  await page.screenshot({ path: 'test-results/base-publica-mobile.png', fullPage: true });
  const result = await (await request.post('/api/contatos/buscar', { data: {} })).json();
  for (const item of result.itens) expect(Object.keys(item).sort()).toEqual(['abertura', 'cidade', 'cnpj', 'cod_municipio', 'id', 'oportunidade', 'porte', 'score', 'segmento']);
  expect(result.total).toBe(info.contatos);
  expect((await request.get('/api/downloads?uf=SP')).status()).toBe(403);
  expect((await request.post('/api/contatos/buscar', { data: { filtros: { somente_email: true } } })).status()).toBe(422);
  expect((await request.post('/api/chat?uf=SP', { data: { pergunta: 'Compare os municípios' } })).status()).toBe(403);
  expect((await request.post('/api/coleta', { data: { ufs: ['CE'] } })).status()).toBe(403);
  expect(external).toEqual([]); expect(errors).toEqual([]);
});

test('assistente público mostra CNPJ e análises sem colunas de contato', async ({ page }) => {
  await page.route('**/api/base?*', async route => {
    const response = await route.fetch();
    await route.fulfill({ json: { ...await response.json(), ia_configurada: true } });
  });
  const filters = { termo: '', cidade: '', cidades: [], segmento: '', segmentos: [], porte: '', bairro: '', ano_minimo: null, ano_maximo: null, score_minimo: 0, somente_celular: false, somente_email: false, somente_sem_dominio: false, ordem: 'score' };
  await page.route('**/api/chat?*', route => route.fulfill({ json: {
    texto: 'Veja o recorte do Ceará.', fontes: [], modelo: 'simulado', resultados: [
      { tipo: 'empresas', titulo: 'Empresas encontradas', filtros: filters, total: 1, itens: [{ id: 1, cnpj: '00000000000191', cidade: 'Fortaleza', cod_municipio: '2304400', segmento: 'Padaria', oportunidade: 'Serviços digitais', porte: 'Microempresa', abertura: '2020', score: 60 }] },
      { tipo: 'analise', titulo: 'Análise', filtros: filters, agrupar_por: ['cidade'], itens: [{ cidade: 'Fortaleza', contatos: 10, score_medio: 60 }] }
    ]
  } }));
  await page.goto('/'); await enterProject(page);
  await page.getByRole('navigation').getByRole('button', { name: 'Assistente', exact: true }).click();
  await expect(page.locator('.suggestions')).not.toContainText('celular');
  await page.getByLabel('Sua pergunta').fill('Compare os municípios');
  await page.getByRole('button', { name: 'Enviar pergunta', exact: true }).click();
  await expect(page.locator('.chat-result .company-name')).toHaveText('00000000000191');
  await expect(page.locator('.chat-result thead').last()).not.toContainText('Com celular');
  await expect(page.locator('.chat-result thead').last()).not.toContainText('Com e-mail');
  await page.locator('.chat-result .company-name').click();
  await expect(page.getByRole('dialog')).not.toContainText('E-mail');
});
