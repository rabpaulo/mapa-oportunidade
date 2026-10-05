# Verificação da migração — 05/10/2026

A migração preserva a interface portuguesa existente, a base completa do Ceará e os contratos das funcionalidades mantidas. Angular 22.2.1 substitui Next.js/React; ASP.NET Core / .NET 10 substitui FastAPI. A coleta Python continua local. Exportações e tarefas de atualização pelo navegador foram removidas.

## Resultados locais

| Verificação | Resultado |
| --- | --- |
| `.venv/bin/python -m pytest -q` | 19 testes aprovados: coleta, importação, travas, publicação atômica e releases |
| `dotnet test server/Ceara.Tests` | 42 testes aprovados: banco/API, origem HTTPS de preview, Gemini simulado e restauração de snapshots |
| `npm run typecheck` | Aprovado, incluindo os templates Angular |
| `npm run build` | Aprovado; bundle inicial de 500,18 kB, mapa carregado separadamente |
| `npm run test:e2e` | 8 fluxos aprovados contra a aplicação de produção local |
| Comparação com o backend Python anterior | 24 páginas de busca, 5 análises e 5 consultas de facetas com resultados iguais na base completa |
| Smoke HTTP da produção nativa e do container | 25 verificações aprovadas em cada execução |
| `git diff --check` | Aprovado |

A comparação cobre os cinco modos de ordenação, paginação, busca por acentos/prefixos e filtros combinados por município, ramo, bairro, ano e canais de contato. Os testes de Gemini usam respostas simuladas para ferramentas, histórico, assinatura de raciocínio, credenciais rejeitadas, cota, timeout e privacidade dos registros individuais. Não foi realizada uma chamada a um provedor Gemini real nesta verificação.

Os fluxos Playwright verificam mapa real, malha offline e falhas de tiles, seleção/comparação de municípios, empresas e detalhes, filtros/paginação, ramos, Base somente leitura, conversa preservada entre abas, erros de cota, persistência de tema e layout móvel. O fluxo de detalhes também verifica abertura pelo teclado, foco preso no diálogo, Escape e devolução do foco à empresa.

O baseline anterior tinha 30 testes Python, incluindo API, IA e exportações. A coleta mantém seus testes em Python; API/IA foram portadas para .NET e os testes de exportação foram removidos junto com a funcionalidade. As contagens representam suítes diferentes.

## Snapshot e runtime

- Versão fixada: `2026-09-14/2026-10-04 17:12`.
- Base: 680.298 empresas, 184 municípios, 147 ramos; SQLite de 365.309.952 bytes.
- Snapshot Brotli: 76.556.932 bytes; checksum SHA-256 `c5265d2eec1f596fc307c0f92cacd348f4c46b7432a93c41abe1edf3a16a4217`.
- Imagem de runtime Linux x64: 246.276.868 bytes, ASP.NET Core 10.0.12, usuário `app`, sem SDK, Node ou Python.
- Container testado com raiz somente leitura, rede desabilitada e `/tmp` em tmpfs de 512 MB. A base fica fora de `wwwroot`; banco e snapshot não são acessíveis via HTTP.
- Startup a frio até a porta 8080 ficar disponível: 4,26 segundos, incluindo a criação do container e a restauração/validação.
- RSS do processo depois dos smoke checks: 120.224 kB (aproximadamente 117 MiB). Esse número não inclui páginas do tmpfs; os arquivos restaurados ocupam aproximadamente 349 MiB. São medições locais, não limites ou previsões de Vercel.

O launcher nativo foi verificado em desenvolvimento e produção. O encerramento por SIGTERM libera os serviços de desenvolvimento. A produção nativa reúne Angular e API na porta 3000 e também restaura o snapshot fixado.

Uma verificação isolada do launcher com CLI Docker simulado confirmou que configurações Gemini fornecidas pelo shell chegam ao processo Docker sem incluir credenciais nos argumentos. Caminhos de dados do host não são encaminhados à produção, evitando sobrescrever o destino privado do snapshot. O encaminhamento usa os valores já analisados de `.env`, com precedência para o ambiente do shell.

## Limitações de implantação

Nenhum projeto Vercel foi criado e nenhum Blob foi usado, conforme solicitado. O snapshot comprimido está preparado localmente e ignorado pelo Git; apenas manifesto, versão e checksums fazem parte do código. Um checkout novo precisa preparar o snapshot antes do build ou configurar uma URL HTTPS imutável para o arquivo Brotli.

O ambiente de verificação não permite criar/entrar nos namespaces de rede necessários ao build Docker com acesso à internet (`operation not permitted`). O build multi-stage completo foi tentado e não pôde concluir o restore nesse ambiente. Angular e C# foram compilados nativamente com as mesmas versões e opções; os resultados foram colocados no estágio final chiseled do Dockerfile, construído e executado com sucesso. O build completo deve ser repetido em um daemon Docker com rede disponível antes do rollout.

O roteamento e os headers HTTPS de preview têm cobertura local; um preview Vercel real e suas medições de startup/memória/disco continuam pendentes. A API já limita chat a cinco chamadas por IP por minuto em cada instância. A regra equivalente no WAF precisa ser aplicada ao futuro projeto para proteção no edge, conforme o [guia de rate limiting da Vercel](https://vercel.com/docs/vercel-firewall/vercel-waf/rate-limiting).

As configurações de container e porta seguem o [guia ASP.NET Core da Vercel](https://vercel.com/kb/guide/dot-net-asp-net-on-vercel-with-docker); a plataforma [Container Images](https://vercel.com/docs/functions/container-images) está em beta. Confirme os limites da conta e a regra WAF no preview antes de produção.
