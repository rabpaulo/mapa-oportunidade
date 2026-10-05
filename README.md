# Mapa de Oportunidades Ceará

Aplicação pública em português para explorar empresas do Ceará, comparar municípios e ramos e conversar com um assistente integrado ao Gemini. Frontend **Angular 22**, API **ASP.NET Core / .NET 10 LTS** e SQLite com FTS5. A coleta e a preparação continuam em Python, executadas somente localmente.

A interface mantém os temas claro/escuro, o mapa MapLibre com malhas locais e fallback sem tiles externos, filtros combinados, paginação, detalhes de empresas e histórico de conversa durante a sessão. A aba Base apresenta a origem e a versão dos dados. Exportações e atualização pelo navegador foram removidas.

## Desenvolvimento

Requisitos: Node.js 26 (ou uma versão compatível com Angular 22), npm e Docker com daemon ativo. A API de desenvolvimento roda em Docker; o Angular usa proxy para `/api`.

```bash
./iniciar.sh
```

Abra `http://localhost:3000`. Ctrl+C encerra ambos os serviços. Com o SDK .NET 10 instalado, é possível usar `./iniciar.sh --nativo`. Os dados vêm de `data/` ou de `CEARA_DATA_DIR` no ambiente/arquivo `.env`.

Para o assistente, copie `.env.example` para `.env` e configure `GEMINI_API_KEY` e, opcionalmente, `GEMINI_MODEL`. A chave fica exclusivamente na API; nunca é incorporada aos assets Angular nem à imagem de produção. No deploy, use variáveis de ambiente do servidor.

O Gemini recebe perguntas, histórico textual, metadados, categorias e agregados. Resultados de ferramentas com identificação e contatos de empresas são enviados diretamente para a interface; esses registros não entram na resposta da ferramenta enviada ao modelo. Perguntas gerais usam o conhecimento do modelo, sem pesquisa na internet. O limite local é de cinco chamadas ao chat por IP por minuto; a aplicação apresenta erros de limite e de cota do provedor.

## Coleta local

```bash
./scripts/preparar_python.sh
.venv/bin/python scripts/coletar.py
# Reprocessar os arquivos já coletados:
.venv/bin/python scripts/coletar.py --reaproveitar
# Importar uma base CE compatível sem sobrescrever dados existentes:
.venv/bin/python scripts/importar_base.py --origem /caminho/para/base
```

A Receita distribui arquivos nacionais, cujo download pode consumir vários GB. Apenas o Ceará é preservado. A coleta usa uma trava entre processos, prepara uma geração temporária, valida o SQLite e os códigos IBGE e publica atomicamente. Leitores da geração anterior continuam com seu snapshot até encerrar a consulta. Python e arquivos de origem não fazem parte do runtime hospedado.

## Release e container

Por enquanto, o projeto usa **snapshot local, sem Vercel Blob**. O banco não entra no Git. O release usa a API de backup do SQLite, verifica integridade/FTS/municípios, inclui as duas malhas GeoJSON e gera um arquivo Brotli reproduzível com SHA-256 do arquivo e de cada item.

```bash
.venv/bin/python scripts/preparar_release.py --saida releases/ce-nova-versao --fixar
./iniciar.sh --producao
# Alternativa com .NET instalado:
./iniciar.sh --nativo --producao
```

`--fixar` grava apenas versão, URL opcional e checksums em `deployment/snapshot.json`, e copia o arquivo comprimido para `deployment/snapshot-input/snapshot.tar.br` (ignorado pelo Git). Escolha um diretório de release novo a cada execução; versões existentes não são sobrescritas.

O Dockerfile compila Angular e C# em estágios separados. A imagem final contém ASP.NET Core, assets e snapshot comprimido, sem Node, Python, SDK ou chave Gemini. No primeiro startup, a API confere os checksums, extrai somente os três arquivos permitidos em `/tmp/ceara-data`, valida banco, versão e malhas e só então inicia o servidor. A pasta fica fora de `wwwroot`; conexões SQLite são somente leitura. Reinícios no mesmo container reutilizam a geração já validada.

```bash
docker build -f Dockerfile.vercel -t ceara:local .
docker run --rm -p 127.0.0.1:8080:8080 -e GEMINI_API_KEY -e GEMINI_MODEL ceara:local
```

Com Docker direto, as variáveis Gemini são lidas do ambiente do shell. O launcher também lê `.env` e encaminha os valores ao processo do servidor. `CEARA_DATA_DIR` local define somente a montagem de desenvolvimento; a produção restaura o snapshot na pasta privada da imagem.

A versão completa atual tem 680.298 empresas, 184 municípios e 147 ramos. O cadastro é de 14/09/2026; os dados foram gerados em 04/10/2026. O snapshot local fica fixado pelo manifesto; futuras coletas devem produzir um novo release e uma nova imagem.

## Vercel

`vercel.json` configura um serviço `container` e encaminha todos os caminhos ao ASP.NET Core, que serve Angular e `/api` no mesmo domínio. O `PORT` deve ser **8080**, também nas configurações do projeto. Containers e Services utilizam recursos beta. Consulte o [guia ASP.NET Core da Vercel](https://vercel.com/kb/guide/dot-net-asp-net-on-vercel-with-docker).

Nenhum projeto Vercel foi criado ou publicado nesta migração. Sem armazenamento remoto, builds a partir somente do Git não têm o snapshot: prepare-o localmente antes de enviar o contexto ou publicar uma imagem pré-construída. `.vercelignore` preserva o arquivo preparado e exclui dados originais, dependências, caches e segredos. A compressão Brotli mantém o contexto abaixo do limite de upload de 100 MB do plano Hobby. Verifique o tamanho após cada novo release; a base pode crescer. Veja os [limites da Vercel](https://vercel.com/docs/limits). Uma URL HTTPS imutável pode ser fixada no manifesto posteriormente; o build baixa o snapshot e verifica seu checksum, sem depender de Blob.

Antes de produção, crie um preview e verifique startup a frio, consumo de memória/disco, `/`, `/api/saude`, buscas, malhas e mesma origem no domínio do preview. Configure `GEMINI_API_KEY` somente no servidor e uma regra de Firewall: caminho igual a `/api/chat`, método POST, janela fixa de 60 segundos, cinco solicitações, chave IP, ação 429. Essa regra precisa ser aplicada ao projeto Vercel; o limitador da API protege cada instância e não substitui o limite no edge. Os contadores WAF são regionais, conforme a [documentação de rate limiting](https://vercel.com/docs/vercel-firewall/vercel-waf/rate-limiting).

Atualize dados com coleta local → release → nova imagem → preview → publicação. Rollback restaura o deployment anterior com seu snapshot incorporado; mantenha os releases antigos para reconstrução. Não substitua um arquivo remoto já referenciado por um manifesto publicado.

## Verificação

```bash
.venv/bin/python -m pytest -q
dotnet test server/Ceara.Tests
cd app
npm ci
npm run typecheck
npm run build
npx playwright install chromium
# Com a aplicação em execução:
npm run test:e2e
```

A API preserva os contratos portugueses de saúde, base, busca, detalhes, facetas, municípios, ramos, análises, malhas e chat. SQL é parametrizado; dimensões, métricas e ordenação usam listas permitidas. Consultas têm orçamento de 15 segundos e chamadas Gemini têm timeout de 45 segundos por rodada, com até cinco rodadas. A especificação fica em `/api/openapi.json`.

Os testes .NET cobrem busca com acentos/prefixos, filtros combinados, paginação, ordenação, agregados, facetas, validação, bancos ausentes/inválidos, origem, privacidade das ferramentas Gemini, erros do provedor, cotas, timeout e validação do snapshot. Os testes Python preservam coleta/importação, travas e publicação atômica e verificam os releases. Playwright cobre mapas, fallback offline, empresas, detalhes, ramos, chat, temas, layout móvel e ausência de controles de exportação/atualização.

Os resultados da migração, medidas do container e limitações de implantação estão em [Verificação da migração](docs/migration-verification.md).

## Fontes e licença

Dados Abertos CNPJ da Receita Federal; geometrias e códigos de municípios do IBGE; tiles externos do OpenFreeMap/OpenStreetMap, com fallback para as malhas locais. A base reúne estabelecimentos ativos na data do cadastro, com e-mail ou celular aproveitável. “Sem domínio próprio” deriva do e-mail e não comprova ausência de site; score é uma priorização para serviços digitais.

**AGPL-3.0-only**, conforme [LICENSE](LICENSE). Os componentes originais de processamento, critérios comerciais e cálculos geométricos são copyright © 2026 Ivo Braatz. Consulte [NOTICE](NOTICE).
