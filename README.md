# Mapa de Oportunidades Ceará

Aplicação web local para explorar empresas do Ceará, comparar municípios e ramos, exportar contatos e conversar com um assistente generalista integrado ao Google Gemini.

Derivada do [Garimpo](https://github.com/ivobraatz/garimpo), de Ivo Braatz. Interface Next.js/React/TypeScript, API FastAPI/Python, banco SQLite com FTS5 e mapas MapLibre com malhas locais do IBGE. Nenhum componente depende de Tauri ou Rust.

## Iniciar

Requisitos: Linux, Python 3.11 ou superior, Node.js 20.9 ou superior e npm.

```bash
./iniciar.sh
```

O comando prepara as dependências na primeira execução e inicia os dois serviços. Abra **http://localhost:3000**. `Ctrl+C` encerra a interface, a API e seus processos filhos. As portas 3000 e 8000 precisam estar livres.

Para compilar e executar a versão de produção:

```bash
./iniciar.sh --producao
```

Os serviços escutam somente em `127.0.0.1`. A interface encaminha `/api/*` para a API local; não há conta, login, hospedagem ou publicação automática.

## Preparar os dados

Este ambiente já possui uma cópia independente da base existente do Ceará: **680.298 contatos, 184 municípios e 147 ramos**, cadastro de **14/09/2026**. Os números mudam quando o cadastro é atualizado.

Se o projeto for clonado em outra máquina e `~/garimpo/data/uf/CE/contatos.db` estiver disponível, o comando de inicialização importa a base automaticamente. Para importar de outro lugar:

```bash
.venv/bin/python scripts/importar_base.py --origem /caminho/para/garimpo
```

A importação usa `sqlite3.Connection.backup`, aceita somente CE e não sobrescreve uma base existente. Copia também o recorte processado da Receita e as malhas em cache. Não compartilha o banco com o Garimpo original.

Sem base de origem, abra **Base**, marque **Baixar novo cadastro** e inicie a atualização. O download original é nacional e pode consumir vários GB, embora apenas o recorte CE seja armazenado. Sem essa opção, o app regenera a base a partir dos arquivos locais.

Cada atualização ocorre em uma pasta temporária. O banco é validado antes de substituir atomicamente a versão anterior. Consultas continuam disponíveis durante o processamento. Uma falha na geração ou validação preserva a base anterior. Reiniciar o servidor interrompe o acompanhamento da tarefa; inicie novamente pela aba Base. Pastas temporárias deixadas por um encerramento forçado não são utilizadas como base.

### Coleta igual ao Garimpo

O coletor usa os módulos Python adaptados de `~/garimpo/src/receita.py` e `ingestar_receita.py`, com o recorte fixo em **CE**. Descobre a publicação mais recente no [espelho da Casa dos Dados](https://dados-abertos-rf-cnpj.casadosdados.com.br/arquivos/), baixa as tabelas de municípios/CNAEs e os dez ZIPs de Estabelecimentos. Filtra `UF=CE` e situação cadastral `02` (ativa), gravando o CSV compactado em fluxo. Depois percorre os dez ZIPs de Empresas e guarda as razões sociais e portes dos CNPJs básicos encontrados. Cada ZIP nacional é apagado após a leitura.

O processamento aplica os critérios originais de nomes, e-mails, telefones, ramos e score, monta o SQLite e o índice FTS5, associa os municípios ao IBGE e prepara as malhas. A publicação só ocorre depois da validação.

Na interface, use **Base → Baixar novo cadastro → Baixar e atualizar Ceará**. Pelo terminal, após preparar o ambiente Python, o mesmo fluxo pode ser executado sem iniciar o servidor web:

```bash
# Coleta completa e publicação segura da nova base CE
.venv/bin/python scripts/coletar.py

# Diretório alternativo ou regeneração dos recortes já baixados
.venv/bin/python scripts/coletar.py --dados /caminho/para/dados
.venv/bin/python scripts/coletar.py --reaproveitar
```

Os logs de download e processamento aparecem no terminal. O comando retorna código `0` quando conclui, `1` em caso de erro e `130` ao interromper com `Ctrl+C`, encerrando o processo de coleta e preservando a base anterior. Uma trava por diretório impede que o terminal e a interface publiquem duas atualizações simultâneas. Os módulos em `src/` continuam disponíveis para uso individual em um diretório de trabalho; `scripts/coletar.py` é a entrada recomendada para preservar a base anterior.

```text
data/receita/       recorte CE e tabelas auxiliares
data/uf/CE/        contatos.db e relatório da geração
data/ibge/         malhas e municípios em cache
```

Para mudar o armazenamento, configure `CEARA_DATA_DIR` no `.env`. Uma importação explícita deve então usar `--destino /esse/mesmo/caminho`. Dados, ambientes e chaves são ignorados pelo Git.

## Configurar a IA gratuita

1. Crie um projeto e uma chave em [Google AI Studio](https://aistudio.google.com/apikey), usando o nível gratuito.
2. Copie o exemplo e edite o arquivo local:

```bash
cp .env.example .env
```

```dotenv
GEMINI_API_KEY=sua_chave
GEMINI_MODEL=gemini-3.5-flash-lite
```

3. Reinicie `./iniciar.sh`.

A chave é carregada apenas pelo backend. Não a coloque em variáveis `NEXT_PUBLIC_*`, no frontend ou no Git. A interface mostra somente se a IA está configurada.

O modelo padrão possui entrada e saída gratuitas na [tabela de preços do Gemini](https://ai.google.dev/gemini-api/docs/pricing). As [cotas variam por projeto](https://ai.google.dev/gemini-api/docs/rate-limits); a aplicação trata esgotamento de quota, chave recusada, indisponibilidade e timeout, sem mudar para serviços pagos ou ativar faturamento. Uma chave vinculada a um projeto pago segue as condições daquele projeto.

O assistente aceita perguntas gerais, redação, explicações e aconselhamento. Para perguntas sobre a base, usa ferramentas estruturadas com filtros validados e SQL parametrizado. Não executa SQL arbitrário, não altera dados e não pesquisa na internet.

O Gemini recebe perguntas e histórico da conversa, categorias e resultados agregados. Nomes, CNPJs, e-mails, telefones e endereços retornados pelas buscas são apresentados diretamente na aplicação, sem integrar o retorno enviado ao modelo. Evite escrever informações pessoais ou confidenciais nas próprias perguntas: os [termos do serviço gratuito](https://ai.google.dev/gemini-api/terms) permitem uso do conteúdo enviado para melhorar produtos do Google. A conversa permanece na memória da página durante a sessão e pode ser apagada em **Nova conversa**.

## Explorar e exportar

- **Mapa:** Ceará e seus municípios, com comparação por quantidade, celular, e-mail sem domínio e score. A lista oferece acesso por teclado aos mesmos municípios do mapa.
- **Empresas:** busca por prefixos sem acentos, filtros combinados, paginação, seleção entre páginas e detalhes. Filtros podem ser recolhidos.
- **Ramos:** volume, qualidade de contato e score por agrupamento comercial.
- **Assistente:** conversa geral, análises e tabelas locais; respostas baseadas em dados incluem versão e filtros.
- **Base:** origem, versão, regeneração e logs.

A exportação respeita todos os filtros da busca. Havendo seleção explícita, exporta somente os IDs selecionados, independentemente dos demais filtros. O Excel usa a aba **Contatos** e as colunas do importador DataRunner, com telefones/CNPJs como texto e links para os canais de contato.

Consultas, fontes e mapas funcionam offline após instalar as dependências e preparar os dados. IA e atualização exigem internet. As perguntas gerais usam o conhecimento do modelo; não representam informações atuais verificadas.

## Limites dos dados

A base reúne estabelecimentos ativos na data do cadastro **com e-mail ou celular aproveitável**, e não todas as empresas do Ceará. Dados cadastrais não comprovam operação atual ou intenção de compra.

O score e as sugestões por ramo preservam os critérios originais do Garimpo, voltados a serviços digitais. **Sem domínio próprio** significa e-mail em provedor gratuito e não comprova ausência de site. **Abertura** contém somente o ano. Não há faturamento, número de funcionários ou histórico temporal na base de contatos. Os ramos são agrupamentos derivados de CNAEs.

## Desenvolvimento e validação

```bash
# Testes de coleta CNPJ, API, consultas, exportação, atualização e Gemini simulado
.venv/bin/python -m pytest -q

# Compilação e tipos
npm --prefix app run build
npm --prefix app run typecheck

# Testes no navegador: execute ./iniciar.sh em outro terminal
cd app
npx playwright install chromium
npm run test:e2e
```

Os testes de coleta usam ZIPs pequenos no layout da Receita e respostas HTTP simuladas, exercitando o download, recorte CE, cruzamento de empresas e geração/publicação real do SQLite. Os testes do Gemini simulam respostas do provedor e erros para não consumir cotas. Uma chamada real exige sua chave local. Os testes de navegador utilizam a base preparada, sem iniciar downloads nacionais. A documentação da API fica em **http://localhost:8000/api/docs**.

## Licença e atribuição

**AGPL-3.0-only**, conforme [LICENSE](LICENSE). Os scripts de processamento, exportação e critérios comerciais foram adaptados do Garimpo, copyright © 2026 Ivo Braatz. O mapa reaproveita o cálculo geométrico do projeto original. As adaptações web e a integração Gemini estão nesta mesma licença.

Dados: Dados Abertos CNPJ da Receita Federal, pelo espelho da Casa dos Dados; códigos e malhas do IBGE. Ao usar a base para contato, considere que cadastros de MEIs podem conter canais pessoais.
