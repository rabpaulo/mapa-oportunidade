import { expect, test } from '@playwright/test';

test('mapa real, seleção de município e persistência dos temas', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', e => errors.push(e.message));
  await page.goto('/');
  await expect(page.getByText('680.298', { exact: true })).toBeVisible();
  await expect(page.locator('.map-canvas canvas')).toBeVisible();
  expect((await page.locator('.map-canvas').boundingBox())?.height).toBeGreaterThan(400);
  await expect(page.locator('.map-status')).toHaveCount(0);
  await expect(page.locator('html')).toHaveAttribute('data-tema', 'escuro');
  await page.locator('.municipality-list button').filter({ hasText: 'Fortaleza' }).click();
  await expect(page.getByRole('heading', { name: 'Fortaleza', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Explorar empresas', exact: true }).click();
  await expect(page.locator('.contacts-table tbody tr')).toHaveCount(50);
  await expect(page.getByRole('combobox').filter({ has: page.locator('option[value="Fortaleza"]') }).first()).toHaveValue('Fortaleza');
  await page.getByRole('button', { name: 'Ativar tema claro' }).click();
  await expect(page.locator('html')).toHaveAttribute('data-tema', 'claro');
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-tema', 'claro');
  await page.getByRole('button', { name: 'Ativar tema escuro' }).click();
  await expect(page.locator('html')).toHaveAttribute('data-tema', 'escuro');
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-tema', 'escuro');
  expect(errors).toEqual([]);
});

test('mapa offline preserva seleção, comparação e acesso às empresas', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.route('https://tiles.openfreemap.org/**', route => route.abort());
  await page.goto('/');
  await expect(page.locator('.map-status')).toHaveCount(0);
  await expect(page.locator('.map-source')).toContainText('ruas indisponíveis');
  await page.getByRole('button', { name: 'Mapa offline', exact: true }).click();
  await expect(page.locator('.map-source')).toContainText('disponível offline');
  await page.getByRole('button', { name: 'Selecionar Sobral', exact: true }).click();
  await expect(page.locator('.map-city-card')).toContainText('Sobral');
  await page.getByLabel('Comparar por').selectOption('score_medio');
  await expect(page.locator('.map-legend')).toContainText('Score médio');
  await expect(page.locator('.map-legend')).not.toContainText('Escala logarítmica');
  await expect(page.locator('.map-city-card')).toContainText('Sobral');
  await page.getByRole('button', { name: 'Ativar tema claro' }).click();
  await expect(page.locator('.map-status')).toHaveCount(0);
  await expect(page.locator('.map-city-card')).toContainText('Sobral');
  await page.getByRole('button', { name: 'Enquadrar Ceará' }).click();
  await expect(page.locator('.map-city-card')).toHaveCount(0);
  await page.getByLabel('Buscar município').fill('Fortaleza');
  await expect(page.locator('.municipality-list li')).toHaveCount(1);
  await page.locator('.municipality-list button').click();
  await page.getByRole('button', { name: 'Ver empresas de Fortaleza' }).click();
  await expect(page.locator('.contacts-table tbody tr')).toHaveCount(50);
  await expect(page.locator('.active-filters')).toContainText('Fortaleza');
  expect(errors).toEqual([]);
});

test('falha dos tiles de ruas mantém a malha local utilizável', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.route('https://tiles.openfreemap.org/planet', route => route.fulfill({ status: 503, body: 'Mapa de ruas indisponível' }));
  await page.goto('/');
  await expect(page.locator('.map-source')).toContainText('ruas indisponíveis');
  await expect(page.locator('.map-status')).toHaveCount(0);
  await page.getByRole('button', { name: 'Selecionar Fortaleza', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Fortaleza', exact: true })).toBeVisible();
  await page.getByLabel('Comparar por').selectOption('com_celular');
  await expect(page.locator('.map-legend')).toContainText('Com celular');
  expect(errors).toEqual([]);
});

test('filtros, detalhes e paginação sem controles de exportação', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('navigation').getByRole('button', { name: 'Empresas', exact: true }).click();
  await expect(page.locator('.contacts-table tbody tr')).toHaveCount(50);
  await page.getByLabel('Buscar empresas').fill('padaria');
  await expect(page.locator('.active-filters')).toContainText('padaria');
  await expect(page.locator('.contacts-table tbody tr')).toHaveCount(50);
  const company = page.locator('.company-name').first();
  await company.focus();
  await page.keyboard.press('Enter');
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await expect(dialog).toContainText('CNPJ');
  await expect(dialog).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(dialog.getByRole('button', { name: 'Fechar detalhes' })).toBeFocused();
  await page.keyboard.press('Shift+Tab');
  await expect(dialog.locator('button, a[href]').last()).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(dialog.getByRole('button', { name: 'Fechar detalhes' })).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(company).toBeFocused();
  await expect(page.locator('.contacts-table input[type="checkbox"]')).toHaveCount(0);
  await expect(page.getByRole('button', { name: /Exportar/i })).toHaveCount(0);
  await page.getByRole('button', { name: 'Próxima página', exact: true }).click();
  await expect(page.locator('.pagination')).toContainText('Página 2');
  await expect(page.locator('.contacts-table tbody tr')).toHaveCount(50);
});

test('ramos abrem um recorte e Base mostra somente origem e versão', async ({ page }) => {
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
  await expect(page.getByRole('button', { name: /Regenerar|Atualizar|Baixar/i })).toHaveCount(0);
  await expect(page.locator('.base-layout input, .base-layout textarea')).toHaveCount(0);
  await expect(page.locator('.base-provenance')).toContainText(['Dados Abertos CNPJ', 'O Gemini recebe']);
});

test('assistente aceita conversa geral, preserva a sessão e mostra análises', async ({ page }) => {
  const requests: unknown[] = [];
  await page.route('**/api/chat', async route => {
    const body = route.request().postDataJSON(); requests.push(body);
    await route.fulfill({ json: { texto: 'Podemos organizar uma pesquisa de mercado. Fortaleza tem 316.136 empresas nesta base.',
      resultados: [{ tipo: 'analise', titulo: 'Comparação', filtros: { cidade: '', cidades: [], segmento: '', segmentos: [], termo: '', porte: '', bairro: '', ano_minimo: null, ano_maximo: null, score_minimo: 0, somente_celular: false, somente_email: false, somente_sem_dominio: false, ordem: 'score' }, agrupar_por: ['cidade'], itens: [{ cidade: 'Fortaleza', contatos: 316136, com_celular: 0, com_email: 0, score_medio: 64 }] }],
      fontes: [{ fonte: 'Receita Federal — recorte CE', versao: '2026-09-14', filtros: { cidade: '', cidades: [], segmento: '', segmentos: [], termo: '', porte: '', bairro: '', ano_minimo: null, ano_maximo: null, score_minimo: 0, somente_celular: false, somente_email: false, somente_sem_dominio: false, ordem: 'score' } }], modelo: 'gemini-simulado' } });
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

test('pergunta bloqueada mostra o motivo e não contamina o histórico', async ({ page }) => {
  const requests: { pergunta: string; historico: unknown[] }[] = [];
  await page.route('**/api/chat', async route => {
    const body = route.request().postDataJSON(); requests.push(body);
    if (body.pergunta.includes('@')) {
      await route.fulfill({ status: 422, json: { detail: 'O filtro de privacidade bloqueou a pergunta ou o histórico. Remova dados pessoais.' } });
    } else {
      await route.fulfill({ json: { texto: 'Vamos comparar os municípios.', resultados: [], fontes: [], modelo: 'gemini-simulado' } });
    }
  });
  await page.goto('/');
  await page.getByRole('navigation').getByRole('button', { name: 'Assistente', exact: true }).click();
  await page.getByLabel('Sua pergunta').fill('Meu e-mail é pessoa@example.invalid');
  await page.getByRole('button', { name: 'Enviar pergunta', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('filtro de privacidade');
  await expect(page.getByLabel('Sua pergunta')).toHaveValue('Meu e-mail é pessoa@example.invalid');
  await expect(page.locator('.chat-message.user')).toHaveCount(0);
  await page.getByLabel('Sua pergunta').fill('Compare Fortaleza e Sobral');
  await page.getByRole('button', { name: 'Enviar pergunta', exact: true }).click();
  await expect(page.locator('.chat-message.model')).toContainText('comparar os municípios');
  expect(requests[1].historico).toEqual([]);
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
