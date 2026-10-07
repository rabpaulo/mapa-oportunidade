# Operação da publicação minimizada

A publicação Vercel usa somente um banco derivado do Ceará com política `empresas-sem-dados-pessoais-v2`. A base nacional integral permanece no computador local. Os campos publicados são id, CNPJ, município/código, atividade, oportunidade, porte, ano de abertura e score. Nomes, contatos, CPF, bairro e endereço não são copiados para o banco, FTS, CSV ou arquivos do release. Empresários individuais, MEIs, naturezas jurídicas não verificadas e registros suprimidos são excluídos.

O minimizador cria um SQLite novo; não apaga colunas de uma cópia que pudesse conservar dados em páginas livres. Downloads do banco são backups dessa base nova. Score e agregados públicos não usam contatos pessoais. A minimização técnica não estabelece sozinha conformidade LGPD; a origem pública também não autoriza automaticamente qualquer reutilização.

## Preparação e publicação

`CEARA_PUBLIC=1` ou `VERCEL=1` exige `CEARA_DATA_PROFILE=minimizado`. O perfil integral e a política antiga que conservava nomes falham antes da exposição. O build valida o snapshot e o runtime repete a validação. Apenas arquivos e tabelas permitidos são aceitos.

```bash
.venv/bin/python scripts/preparar_base_publica.py --saida artifacts/publica-NOVA-VERSAO
.venv/bin/python scripts/preparar_release.py --dados artifacts/publica-NOVA-VERSAO --saida releases/publica-NOVA-VERSAO --publico --fixar
npx vercel deploy
npx vercel promote URL_DA_PREVIA_VALIDADA
```

Verifique `/api/base`, busca/detalhe, downloads, mapa, bloqueio de SP e da coleta local. O assistente deve usar somente ferramentas do esquema público, sem nomes, contatos ou endereços. O banco nacional, origens e segredos são excluídos do upload. Não publique snapshots integrais antigos em links alternativos. Deploys de versões anteriores que exponham dados pessoais precisam ser retirados conforme o atendimento e a política de retenção; não faça rollback para eles.

## Atendimento e supressão

Configure `PUBLIC_RESPONSAVEL` e `PUBLIC_PRIVACY_EMAIL` com identificação real e e-mail monitorado. Sem configuração, a interface informa a pendência; não inventa um responsável. Avalie pedidos de acesso, correção, oposição, bloqueio ou supressão conforme a LGPD. Registre decisões em ambiente privado e peça somente comprovação proporcional, sem exigir CPF completo por padrão.

```bash
.venv/bin/python scripts/preparar_base_publica.py --suprimir-cnpj CNPJ_SEM_PONTUACAO
```

A lista usa hashes do CNPJ e permanece privada. O minimizador reaplica supressões a cada release; um novo release precisa ser preparado e publicado para efetivar alterações. Elas não alteram o cadastro oficial da Receita. Não se promete apagar cópias já obtidas por terceiros.

## Hospedagem e IA

A publicação mantém mapa local, CSP e limites de requisições. O chat usa a chave Gemini do responsável, configurada como segredo no Vercel em Preview e Production; a chave não chega ao navegador e não acompanha os artefatos. A hospedagem ainda pode tratar IP e registros técnicos. Defina finalidade, hipótese legal, retenção, atendimento e mecanismos aplicáveis aos fluxos reais da Vercel. Selecionar São Paulo como região não demonstra residência integral dos tratamentos no Brasil.

Na execução local cada usuário fornece sua própria chave em `.env`. Nos dois modos, o Gemini recebe perguntas, histórico, categorias e agregados, sem registros individuais; a publicação só oferece ferramentas compatíveis com a base minimizada do Ceará. O filtro não garante anonimização de texto livre. Consulte a [pesquisa de fontes oficiais](lgpd-fontes-oficiais.md), que registra também achados históricos da antiga publicação integral.

A configuração `CEARA_DATA_PROFILE` do projeto Vercel também deve ser `minimizado`, tanto em Preview quanto em Production. Um valor antigo no painel pode prevalecer durante a promoção e será rejeitado pelo startup. Atualize-o antes de promover a prévia.

A imagem de produção usa o argumento obrigatório `--publicacao-ceara` no build e na inicialização. Ele ativa a proteção pública independentemente de `CEARA_PUBLIC` no painel; um perfil integral continua sendo rejeitado.
