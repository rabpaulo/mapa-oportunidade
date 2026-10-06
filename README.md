# Mapa de Oportunidades Ceará

Aplicação em português para explorar empresas do Ceará e comparar municípios e ramos. Frontend **Angular 22**, API **ASP.NET Core / .NET 10 LTS** e SQLite com FTS5. A coleta e a preparação da base são executadas localmente em Python.

Site: [mapa-oportunidade-ceara.vercel.app](https://mapa-oportunidade-ceara.vercel.app). Código: [rabpaulo/mapa-oportunidade](https://github.com/rabpaulo/mapa-oportunidade).

A publicação usa a **base local integral: 680.298 registros e todas as 19 colunas**, incluindo contatos e endereços, sem exclusões na preparação do snapshot. O cadastro atual é de 14/09/2026, gerado em 04/10/2026. O banco original é preservado e uma cópia consistente é incorporada à imagem da aplicação. Consultas no site usam essa cópia local no servidor, sem buscar empresas em serviços externos.

**Fonte pública:** Dados Abertos CNPJ da Receita Federal, obtidos pelo espelho Casa dos Dados. Malhas e códigos municipais: IBGE. Informações podem estar desatualizadas. O score é calculado pela aplicação; não é uma avaliação da Receita Federal. A base pode conter dados pessoais associados ao cadastro empresarial; sua origem pública não autoriza automaticamente qualquer reutilização ou prospecção.

## Executar localmente

Requisitos: Git, Node.js 26, npm, Python 3 com suporte a `venv` e Docker com daemon ativo. Como alternativa ao Docker, instale o SDK .NET 10.

```bash
git clone https://github.com/rabpaulo/mapa-oportunidade.git
cd mapa-oportunidade
./scripts/preparar_python.sh
.venv/bin/python scripts/coletar.py
cp .env.example .env
./iniciar.sh
```

Abra **http://localhost:3000**. Ctrl+C encerra os serviços. O launcher instala as dependências Angular se necessário e mantém a API em Docker. Com .NET 10 instalado, use `./iniciar.sh --nativo`.

**O clone contém o código, não o banco nem chaves.** O coletor baixa a fonte pública e prepara os dados somente no seu computador. O download nacional pode consumir vários GB e demorar; o recorte preservado é o Ceará. O coletor usa trava entre processos, valida a geração e publica atomicamente. Novas coletas podem resultar em totais e versões diferentes da publicação atual.

Para reprocessar os arquivos já coletados ou importar uma base CE compatível sem sobrescrever dados existentes:

```bash
.venv/bin/python scripts/coletar.py --reaproveitar
.venv/bin/python scripts/importar_base.py --origem /caminho/para/base
```

Os dados vêm de `data/` ou de `CEARA_DATA_DIR` configurado no ambiente/arquivo `.env`. A aplicação local escuta no endereço de loopback. Executar localmente não dispensa obrigações aplicáveis ao uso dos dados.

## Assistente Gemini

O assistente continua disponível **somente na execução local**. Configure `GEMINI_API_KEY` e, opcionalmente, `GEMINI_MODEL` em `.env`. A chave fica exclusivamente na API; não entra nos assets Angular, no Git ou na imagem publicada.

O Gemini recebe perguntas, histórico, categorias e agregados. Os registros individuais retornados pelas ferramentas aparecem diretamente na interface e não são enviados ao modelo. O filtro local bloqueia padrões comuns de documentos, e-mails, telefones, endereços e nomes rotulados, inclusive antes de cada chamada ao provedor. Pequenos grupos também são omitidos das respostas das ferramentas enviadas ao modelo.

Essas medidas não garantem anonimização: texto livre pode conter informações pessoais não reconhecidas pelo filtro. Não envie dados pessoais ou confidenciais. O chat público responde HTTP 403 mesmo que uma chave exista no ambiente da Vercel.

## Perfis de dados e hospedagem

`CEARA_PUBLIC=1` ativa proteções de hospedagem: limites por IP e instância, política de conteúdo, mapa somente com malhas locais e bloqueio do chat. A configuração dos dados é independente:

| `CEARA_DATA_PROFILE` | Comportamento |
| --- | --- |
| `integral` | Consulta o snapshot original e devolve todas as 19 colunas. É o perfil publicado na Vercel. |
| `minimizado` | Exige a base derivada, sem contatos e endereços, e usa a lista de campos permitidos. |
| Não configurado | Mantém compatibilidade: minimizado em hospedagem pública, integral localmente. |

A interface mantém temas claro/escuro, filtros, paginação, detalhes e botões de contato. O indicador de domínio próprio deriva do e-mail e não comprova presença ou ausência de site. A base não possui URLs de sites confirmados. Não há cadastro, analytics ou exportação pelo navegador.

Configure `PUBLIC_RESPONSAVEL` e `PUBLIC_PRIVACY_EMAIL` com identificação real e contato público monitorado. Esses dados ainda estão pendentes na publicação; não se afirma conformidade completa com a LGPD. Veja [operação e privacidade](docs/operacao-privacidade.md) e [pesquisa jurídica](docs/lgpd-fontes-oficiais.md).

## Preparar release e publicar

O banco e os arquivos de origem ficam fora do Git. O release usa backup SQLite, valida integridade, FTS e municípios, inclui as duas malhas GeoJSON e gera um arquivo Brotli com checksums SHA-256.

```bash
# Snapshot integral da base existente, sem passar pelo minimizador:
.venv/bin/python scripts/preparar_release.py --dados data --saida releases/ce-NOVA-VERSAO --fixar
```

Escolha um diretório novo a cada release. `--fixar` grava versão e checksums em `deployment/snapshot.json` e copia o arquivo comprimido para `deployment/snapshot-input/snapshot.tar.br`, ignorado pelo Git. O banco original não é modificado.

```bash
# Conferir o snapshot em produção local:
CEARA_PUBLIC=1 CEARA_DATA_PROFILE=integral ./iniciar.sh --nativo --producao
# Ou com Docker:
docker build -f Dockerfile.vercel -t ceara:local .
docker run --rm -p 127.0.0.1:8080:8080 -e CEARA_PUBLIC=1 -e CEARA_DATA_PROFILE=integral ceara:local
```

O startup valida hashes, extrai somente os três arquivos permitidos, confere banco e malhas e inicia o servidor. A pasta dos dados fica fora de `wwwroot`; SQLite é somente leitura. A imagem final contém ASP.NET Core, assets, snapshot e código correspondente, sem SDK, Node, Python ou chaves.

### Vercel

`vercel.json` usa serviço em container, mesma origem para Angular e `/api`, porta 8080, região `gru1`, `CEARA_PUBLIC=1` e `CEARA_DATA_PROFILE=integral`. Essa região não garante que todos os tratamentos da hospedagem ocorram no Brasil.

**O deploy é manual**, pois o snapshot não acompanha o Git. Deploys automáticos por push estão desativados para evitar builds sem o banco. Após preparar e fixar o release:

```bash
npx vercel deploy
# Após conferir a prévia:
npx vercel promote URL_DO_DEPLOYMENT
```

A prévia precisa das mesmas variáveis de hospedagem e perfil integral. Confira `/api/saude`, `/api/base`, busca, detalhes, mapa, aviso de origem e links do código antes da promoção. O contexto de upload inclui o snapshot fixado; dados originais, releases, caches, dependências e segredos continuam excluídos.

Cada deployment oferece o código correspondente em `/codigo-fonte.tar.gz`, sem banco ou segredos. O rodapé também aponta para o GitHub público e as instruções locais.

### Perfil minimizado opcional

```bash
.venv/bin/python scripts/preparar_base_publica.py --saida artifacts/publica-NOVA-VERSAO
.venv/bin/python scripts/preparar_release.py --dados artifacts/publica-NOVA-VERSAO --saida releases/publica-NOVA-VERSAO --fixar
```

Esse perfil requer `CEARA_DATA_PROFILE=minimizado`. O minimizador e suas regras continuam disponíveis como opção; não são usados pelo snapshot integral publicado.

## Verificação

```bash
.venv/bin/python -m pytest -q
dotnet test server/Ceara.Tests
cd app
npm ci
npm run typecheck
npm run build
npx playwright install chromium
# Com a aplicação local em execução:
npm run test:e2e -- flows.spec.ts
# Contra a publicação integral:
CEARA_BASE_URL=https://mapa-oportunidade-ceara.vercel.app npm run test:e2e -- public.spec.ts
```

Os testes cobrem consultas, agregados, snapshot, transporte Gemini, perfil integral e compatibilidade com o minimizado. Playwright verifica detalhes completos, mapa local, avisos, código e layout móvel. A API documenta os contratos portugueses em `/api/openapi.json`.

## Fontes e licença

Dados Abertos CNPJ da Receita Federal pelo espelho Casa dos Dados; geometrias e códigos municipais do IBGE. A base local inclui estabelecimentos com contato e não é um censo de todas as empresas do Ceará.

**AGPL-3.0-only**, conforme [LICENSE](LICENSE). Os componentes originais de processamento, critérios comerciais e cálculos geométricos são copyright © 2026 Ivo Braatz. Consulte [NOTICE](NOTICE).
