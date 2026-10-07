# Mapa de Oportunidades

Aplicação em português com Angular, ASP.NET Core / .NET 10 e SQLite com FTS5. A execução local permite coletar, explorar e baixar dados das **27 UFs**. A publicação no [Vercel](https://mapa-oportunidade-ceara.vercel.app) disponibiliza **somente o Ceará**, com um banco público separado e minimizado.

A coleta adapta o motor do Garimpo: baixa os ZIPs nacionais da Receita pelo espelho Casa dos Dados, distribui os estabelecimentos ativos pelas UFs selecionadas e gera um banco por estado. O recorte de oportunidades exige e-mail ou celular aproveitável e nome utilizável; **não representa todas as empresas brasileiras**. Não são coletados sócios ou representantes. CNPJ permanece texto, incluindo zeros e o formato alfanumérico.

## Executar localmente

Requisitos: Node.js compatível com `app/package.json`, Python 3 com venv e Docker com daemon ativo; como alternativa ao Docker, SDK .NET 10.

```bash
./scripts/preparar_python.sh
cp .env.example .env
# Ceará por padrão:
.venv/bin/python scripts/coletar.py
# Várias UFs com um único download nacional:
.venv/bin/python scripts/coletar.py --uf CE SP MG
# Brasil inteiro:
.venv/bin/python scripts/coletar.py --todos
./iniciar.sh
# Alternativa ao Docker:
./iniciar.sh --nativo
```

Abra **http://localhost:3000**. Na aba **Base**, selecione qualquer uma das 27 UFs, ou todas, e clique em **Baixar estados**. A coleta segue em segundo plano, com log, mesmo ao navegar por outras abas. Também é possível usar os comandos acima. O seletor do cabeçalho permite navegar pelas UFs já preparadas. Trocar de estado limpa filtros, detalhes e histórico do assistente. Não há banco ou chaves no clone. A aplicação abre mesmo sem base; entre na aba Base para fazer a primeira coleta.

Os bancos ficam em `data/uf/<UF>/contatos.db`, os recortes em `data/receita/` e as malhas do IBGE em `data/ibge/`. `CEARA_DATA_DIR` permite escolher outra unidade. Reserve espaço para as UFs, downloads completos e geração temporária; os arquivos nacionais são processados um por vez. Índices temporários em SQLite evitam carregar os CNPJs e empresas nacionais em memória.

```bash
# Regenerar sem baixar novamente:
.venv/bin/python scripts/coletar.py --uf CE SP --reaproveitar
# Preparar downloads de uma base existente:
.venv/bin/python scripts/preparar_downloads.py --uf CE SP
```

A trava impede duas coletas simultâneas. Todos os bancos selecionados são validados antes da publicação. Cada banco é trocado atomicamente; leitores existentes continuam na versão anterior, e UFs não selecionadas permanecem intactas. Downloads são imutáveis por geração e publicados antes de o banco selecionar essa geração. Divergências de grafia verificadas com o IBGE usam aliases limitados à UF. Um vínculo municipal inconsistente já revisado em SC é preservado nos arquivos e consultas locais, sinalizado na Base e excluído do mapa; outros municípios sem correspondência continuam bloqueando a validação.

## Downloads e API

A aba **Base** local permite coletar UFs e baixar arquivos SQLite, CSV, metadados e malhas GeoJSON. A API mantém o catálogo com versão, quantidade, tamanho e SHA-256. Na Vercel, a Base apresenta somente informações do Ceará e instruções locais. A tela de empresas oferece exportação do recorte completo, sem limitar à página atual. Na publicação, todos os arquivos e exports usam o esquema minimizado. CSVs protegem células que poderiam executar fórmulas em programas de planilha.

- `GET /api/ufs`: estados e disponibilidade; retorna apenas CE na publicação.
- `GET /api/coleta`: estado e log da coleta local.
- `POST /api/coleta`: recebe `{ "ufs": ["CE", "SP"], "reaproveitar": false }` e inicia a coleta local. Coletas simultâneas recebem HTTP 409. Ambas as rotas recebem HTTP 403 no Vercel.
- Consultas existentes aceitam `?uf=SP`; a omissão mantém CE.
- `GET /api/downloads?uf=CE`: catálogo da geração atual.
- `GET /api/downloads/{id}?uf=CE&geracao=...`: arquivo permitido, com suporte a Range. Uma geração desatualizada recebe HTTP 409.
- `POST /api/contatos/exportar?uf=CE`: recebe o mesmo objeto de filtros de `/api/facetas` e devolve CSV gzip em fluxo.

A especificação está em `/api/openapi.json`. Arquivos são resolvidos por IDs fixos; não são aceitos caminhos arbitrários. Consultas de UFs não publicadas recebem HTTP 403; UFs inválidas, HTTP 422. Catálogo ausente indica que os downloads ainda precisam ser preparados.

## Publicação minimizada

Na Vercel, `CEARA_PUBLIC=1`, `VERCEL=1` ou ambos tornam obrigatória a política `empresas-sem-dados-pessoais-v2`. `CEARA_DATA_PROFILE=integral` é rejeitado em hospedagem pública. O padrão local continua integral.

A imagem de produção também exige `--publicacao-ceara` no build e na inicialização. Essa proteção não pode ser desligada pelas variáveis de ambiente do painel.

O banco público é criado do zero e contém apenas **id, CNPJ, município, código municipal, ramo, oportunidade, porte, ano de abertura e score**, além de metadados, agregados e um FTS reconstruído desses campos. Nomes empresariais e pessoais, CPF, contatos, endereço, bairro e indicadores de contato não existem no seu esquema. O mesmo recorte vale para SQLite, CSV, API e interface.

A preparação usa a natureza jurídica da mesma versão da origem para uma lista conservadora de entidades empresariais. Exclui empresários individuais, MEIs, classificações não verificadas e supressões. O score público usa atividade, porte e ano de abertura, sem indicadores de contato. Isso reduz exposição; não é certificação de conformidade LGPD.

```bash
.venv/bin/python scripts/preparar_base_publica.py --saida artifacts/publica-NOVA-VERSAO
.venv/bin/python scripts/preparar_release.py --dados artifacts/publica-NOVA-VERSAO --saida releases/publica-NOVA-VERSAO --publico --fixar
npx vercel deploy
# Após validar a prévia:
npx vercel promote URL_DO_DEPLOYMENT
```

O release usa backup SQLite e hashes de todos os arquivos, incluindo downloads. `--fixar` exige o perfil minimizado. Build e startup validam esquema, política, Ceará, integridade, versão e downloads; snapshots integrais e políticas antigas que conservem nomes são recusados. A imagem mantém dados fora de `wwwroot` e não contém a base nacional local.

Deploys automáticos por push continuam desativados: o artefato fixado é incluído pelo upload manual e fica fora do Git. `/codigo-fonte.tar.gz` oferece o código correspondente, sem bancos ou segredos, conforme a AGPL. O mapa público usa apenas malhas locais. O assistente público usa a chave do responsável no servidor e consulta somente a base minimizada do Ceará.

Para registrar uma supressão persistente:

```bash
.venv/bin/python scripts/preparar_base_publica.py --suprimir-cnpj CNPJ_SEM_PONTUACAO
```

Gere e publique outro release para aplicá-la. Configure `PUBLIC_RESPONSAVEL` e `PUBLIC_PRIVACY_EMAIL` com identificação e canal real monitorado. Veja [operação e privacidade](docs/operacao-privacidade.md) e [fontes oficiais](docs/lgpd-fontes-oficiais.md).

## Assistente Gemini

**Local:** cada usuário configura sua própria `GEMINI_API_KEY` e, opcionalmente, `GEMINI_MODEL` em `.env`, e reinicia a aplicação. O assistente consulta a UF selecionada. Sem chave, coleta, navegação e downloads continuam disponíveis.

**Vercel:** o responsável configura sua `GEMINI_API_KEY` nas variáveis de ambiente do projeto para Preview e Production. Os visitantes usam o assistente sem fornecer chave. O servidor consulta somente o Ceará e oferece à IA apenas filtros e métricas do esquema minimizado, sem nomes, contatos ou endereços.

Em ambos os modos, registros individuais são exibidos diretamente na aplicação; o provedor recebe perguntas, histórico, categorias e agregados. O filtro reduz padrões de documentos e contatos, mas não garante anonimização de texto livre. Não envie dados pessoais ou confidenciais. Chaves ficam no servidor, fora do frontend, do Git, dos snapshots e do pacote de código-fonte. O `.env` local não é enviado no deploy; a chave pública vem exclusivamente da configuração do Vercel.

## Verificação

```bash
.venv/bin/python -m pytest -q
dotnet test server/Ceara.Tests
cd app
npm ci
npm run typecheck
npm run build
# Com servidor local em execução:
npm run test:e2e -- flows.spec.ts national.spec.ts
# Com servidor público minimizado:
npm run test:e2e -- public.spec.ts
```

Os testes cobrem coleta múltipla, falhas, CNPJ textual, isolamento de consultas por UF, exports, hashes, ausência de campos pessoais e recusa de snapshots inseguros.

## Fontes e licença

Dados Abertos CNPJ da Receita Federal pelo espelho Casa dos Dados; códigos e malhas municipais do IBGE. Informações podem estar desatualizadas; score e categorias comerciais são cálculos da aplicação.

**AGPL-3.0-only**, conforme [LICENSE](LICENSE). Copyright © 2026 Ivo Braatz nos componentes originais. Consulte [NOTICE](NOTICE).
