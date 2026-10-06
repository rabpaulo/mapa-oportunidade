> **Estado atual:** o plano aprovado em 05/10/2026 substituiu o recorte minimizado por uma cópia integral da base local e código público no GitHub. A publicação integral não executa o minimizador. As recomendações e medições abaixo registram a auditoria dos estados anteriores; não são uma descrição atual de campos omitidos. Veja [operação atual](operacao-privacidade.md).

# LGPD e publicação do Mapa de Oportunidades Ceará

Pesquisa e inspeção: **5 de outubro de 2026**. Fontes primárias: legislação, ANPD, Receita Federal, Vercel e Google. Este documento é uma avaliação preliminar de riscos e orientações técnicas; não certifica conformidade nem substitui análise jurídica do modelo de negócio e dos contratos efetivamente assinados. Não foram publicados dados. Após a pesquisa inicial, foi implementado o filtro de envio ao Gemini descrito abaixo.

## Conclusão preliminar

**Não há fundamento suficiente para declarar este projeto conforme à LGPD no estado atual.** A publicação na Vercel é possível em princípio, mas abrir o diretório atual ao público amplia riscos identificados abaixo: contatos potencialmente pessoais, empresário individual, sequências compatíveis com CPF, extração por API, falta de procedimentos de direitos e envio de texto livre a um provedor de IA. A plataforma de hospedagem não resolve essas questões por si só.

## O que as fontes oficiais estabelecem

### Cadastro empresarial pode conter dados pessoais

A LGPD protege pessoas naturais. Dados exclusivamente pertencentes a uma pessoa jurídica ficam fora dessa definição; o nome da coluna ou a presença de um CNPJ não decide a natureza de todos os campos. A ANPD considera pessoais as informações associadas a pessoa identificada ou identificável. [ANPD, perguntas frequentes, itens 2.1–2.5](https://www.gov.br/anpd/pt-br/acesso-a-informacao/perguntas-frequentes/perguntas-frequentes).

Telefone pessoal, e-mail identificável e endereço residencial são exemplos oficiais. **Aplicação ao projeto:** contatos de empresário individual/MEI podem coincidir com os da pessoa física; devem ser classificados e avaliados, sem presumir que todo contato empresarial é pessoal ou que todo MEI usa endereço residencial. [ANPD, titular de dados](https://www.gov.br/anpd/pt-br/assuntos/titular-de-dados).

A própria ficha de privacidade do serviço CNPJ reconhece tratamento de nome e CPF de responsáveis/sócios. As bases legais e a finalidade cadastral da Receita dizem respeito à sua atuação: não constituem autorização automática para outra pessoa criar um diretório de prospecção. [Receita Federal, Cadastro CNPJ, atualizado em 2/12/2025](https://www.gov.br/pt-br/lgpd/cadastro-de-pessoas-juricias-cnpj).

### Dados públicos não são uso irrestrito

Os arts. 7º, §§ 3º, 4º, 6º e 7º exigem considerar o contexto da divulgação pública e preservar princípios e direitos, inclusive em novas finalidades. Dispensa de consentimento não dispensa as demais obrigações. Não se deve equiparar divulgação cadastral pela Receita a consentimento para prospecção. Os arts. 6º e 9º exigem necessidade, transparência e informação sobre o tratamento. O art. 4º, I, excepciona apenas fins exclusivamente particulares e não econômicos; gratuidade não basta para enquadrar o projeto. [LGPD consolidada, arts. 4º, 6º, 7º e 9º](https://www.planalto.gov.br/ccivil_03/_ato2015-2018/2018/lei/l13709compilado.htm).

Legítimo interesse pode ser examinado, mas não é justificativa automática. O guia da ANPD orienta testar cada finalidade em três etapas: finalidade, necessidade e balanceamento/salvaguardas. Avaliar a origem pública, a expectativa dos titulares, a intrusividade, alternativas menos invasivas e mecanismos de oposição. Não usar essa base quando prevalecerem os direitos do titular; ela não serve para dados sensíveis. **Aplicação ao projeto:** publicação nominal, disponibilização de contatos, facilitação de WhatsApp e classificação comercial merecem análises próprias; a utilidade comercial não conclui o balanceamento. [ANPD, Guia de legítimo interesse, fev./2024, pp. 22–33 e modelo no Anexo II](https://www.gov.br/anpd/pt-br/centrais-de-conteudo/materiais-educativos-e-publicacoes/copy_of_guia_legitimo_interesse.pdf/@@display-file/file).

### Direitos precisam funcionar na prática

A ANPD explica acesso, correção, informação, oposição nos casos previstos e bloqueio/eliminação de dados desnecessários ou irregulares. Não existe eliminação incondicional de qualquer registro a pedido; a base legal e o art. 16 importam. O prazo de 15 dias refere-se à declaração completa de confirmação/acesso do art. 19, não a um prazo universal de exclusão. Decisões inteiramente automatizadas que afetem interesses podem ensejar revisão e informação sobre critérios. **Aplicação ao projeto:** verificar se o score alcança pessoa natural e produz esse efeito, antes de concluir que o art. 20 se aplica. [ANPD, direitos dos titulares](https://www.gov.br/anpd/pt-br/assuntos/titular-de-dados/direito-dos-titulares).

**Recomendação técnica:** manter canal gratuito de atendimento e mecanismo de supressão/correção no diretório, inclusive nas próximas importações, caches e releases. Isso atua sobre a cópia do projeto e não altera o cadastro legal da Receita. Não condicionar a retirada de dado pessoal indevido à baixa do CNPJ. Uma lista interna de supressões também deve ter acesso restrito e retenção justificada. São propostas de implementação, não obrigações literais de um endpoint específico.

### Hospedagem e IA exigem avaliar o fluxo internacional real

A Resolução CD/ANPD nº 19/2024 exige verificar se existe transferência, sua base legal e o mecanismo válido. Quando adotadas, as cláusulas-padrão ANPD devem ser incorporadas integralmente aos instrumentos contratuais. O prazo de transição de doze meses desde 23/8/2024 terminou em agosto de 2025; em outubro de 2026 ele já não está aberto. [ANPD, Resolução nº 19/2024, art. 2º e Anexo I, arts. 4º, 9º e 16](https://www.gov.br/anpd/pt-br/acesso-a-informacao/institucional/atos-normativos/regulamentacoes_anpd/resolucao-cd-anpd-no-19-de-23-de-agosto-de-2024).

A ANPD reconheceu adequação da União Europeia pela Resolução nº 32/2026. Isso não equivale à aprovação automática das cláusulas europeias para envio do Brasil aos EUA. Na página oficial consultada, a ANPD informa que nenhuma cláusula-padrão estrangeira equivalente havia sido reconhecida. **Aplicação:** escolher um mecanismo adequado a cada fluxo efetivo; não presumir cobertura brasileira por referências genéricas ao GDPR. [ANPD, transferências internacionais, repositório e perguntas frequentes](https://www.gov.br/anpd/pt-br/assuntos/assuntos-internacionais/transferencia-internacional-de-dados).

### Vercel

O DPA público, atualizado em 17/3/2026 e eficaz em 31/3/2026, define responsabilidades do cliente, operadores/suboperadores e mecanismos internacionais. O Schedule 3 identifica cláusulas da Comissão Europeia de 2021 e o aditivo britânico. Não foi localizada incorporação das cláusulas-padrão ANPD nesse texto público; pode existir instrumento privado não examinado. Conferir o contrato realmente aplicável e a lista de subprocessadores antes de transferir dados pessoais. [Vercel, DPA](https://vercel.com/legal/dpa), [Trust Center](https://security.vercel.com/).

A Vercel informa que a região padrão das funções é nos EUA e que pode tratar dados onde ela ou prestadores tenham operações. Selecionar uma região para a aplicação não prova residência integral dos dados no Brasil. [Vercel, Security & Compliance Measures, atualizado em 8/9/2026](https://vercel.com/docs/security/compliance).

O aviso de privacidade menciona IP de visitantes, localização aproximada, dados de tráfego e logs. Portanto, o inventário de tratamento deve abranger também visitantes. [Vercel, Privacy Notice, atualizado em 1/6/2026](https://vercel.com/legal/privacy-notice).

Retenção de runtime logs depende do plano e recursos: a documentação atual traz 1 hora no Hobby, 1 dia no Pro, 3 dias no Enterprise e 30 dias com Observability Plus. Isso não demonstra retenção total de todos os sistemas, backups ou integrações. Evitar registrar prompts, contatos e identificadores desnecessários; revisar Log Drains e acessos administrativos. [Vercel, Runtime Logs, atualizado em 28/8/2026](https://vercel.com/docs/logs/runtime).

### Gemini API

Termos vigentes em 23/3/2026: no serviço não pago, Google pode usar entradas/respostas para melhorar produtos e tecnologias de aprendizado de máquina, com revisão humana; o texto orienta não enviar informação pessoal, confidencial ou sensível. No contexto brasileiro, não presumir que as exceções europeias se aplicam. [Google, Gemini API Additional Terms, Unpaid Services](https://ai.google.dev/gemini-api/terms).

Na API, o regime pago exige Cloud Project associado a conta de faturamento ativa. Nesse regime, prompts/respostas não são usados para melhorar produtos, mas há retenção limitada para segurança/cumprimento das regras e processamento transitório em países onde Google/agentes têm instalações. Contrato de tratamento e transferências continuam relevantes. **Aplicação:** confirmar o projeto faturado, minimizar/redigir o texto enviado e informar finalidade, histórico e compartilhamento; cobrar pelo próprio aplicativo não torna a API Google paga. [Google, mesmos termos, Paid Services](https://ai.google.dev/gemini-api/terms).

### Possíveis consequências

Pode haver petição de titular, denúncia, fiscalização e pedido judicial. Não se pode prever multa automática pelo deploy. O art. 42 prevê reparação por dano causado em violação à proteção de dados; o art. 52 prevê advertências, bloqueio, eliminação, suspensão e multas conforme enquadramento. [LGPD consolidada, arts. 42 e 52](https://www.planalto.gov.br/ccivil_03/_ato2015-2018/2018/lei/l13709compilado.htm), [ANPD, titular de dados](https://www.gov.br/anpd/pt-br/assuntos/titular-de-dados).

O limite percentual do art. 52, II, menciona faturamento de pessoa jurídica privada; isso não permite prometer imunidade a pessoa física. A dosimetria também contempla rendimentos de pessoas naturais relacionados ao tratamento (art. 11, § 1º, IV, d) e hipóteses sem faturamento (§ 4º). Enquadramento, processo e circunstâncias concretas importam. [Resolução CD/ANPD nº 4/2023, reprodução oficial do Ministério da Justiça](https://bibliotecadigital.mj.gov.br/bitstream/1/9179/2/RES_ANPD_2023_4.html).

## Evidências técnicas deste repositório

Achados do código e da base local levantados nesta avaliação; não são conclusões feitas pelas fontes jurídicas acima. Nenhum registro identificável é reproduzido aqui.

| Achado | Evidência e implicação |
|---|---|
| Diretório individualizado público | [Database.cs](../server/Ceara.Api/Database.cs), linhas 139–160: busca/detalhe selecionam todas as colunas e removem apenas `busca`. A resposta inclui identidade, contatos e endereço disponíveis na base. [Program.cs](../server/Ceara.Api/Program.cs), linhas 79–85: essas rotas não exigem autenticação ou política de rate limit; o limite explícito está no chat, linha 100. A ausência de autenticação não é ilegal por si, mas facilita extração e aumenta o impacto da divulgação. |
| Empresário individual sem distinção suficiente | [gerar_leads.py](../src/gerar_leads.py), linhas 100–108, 147–178: o mapa de empresas retém razão social/porte e não a natureza jurídica. A auditoria da origem identificou natureza `2135` em 437.178 CNPJs básicos presentes na base; esse número não é uma contagem de estabelecimentos nem de MEIs. O código corresponde a empresário individual, conforme [IBGE/CONCLA](https://cnae.ibge.gov.br/estrutura/natjur-estrutura/natureza-juridica-2003-1/213-5-empresario-individual.html). Não há filtro confiável de MEI no esquema final. |
| Sinais de identificadores pessoais no artefato de deploy | A auditoria somente de leitura da base local e, separadamente, do banco interno do snapshot encontrou 26 registros distintos com sequências de 11 dígitos compatíveis com CPF e dígitos verificadores válidos: 10 em `nome` e 25 em `empresa`, com sobreposição. Os checksums do arquivo Brotli e do banco interno conferem com [snapshot.json](../deployment/snapshot.json); o artefato tem 680.298 linhas, versão Receita `2026-09-14` e geração `2026-10-04 17:12`. É um indicador técnico, não confirmação de CPFs emitidos ou da identidade de pessoas. **Recomendação: bloquear publicação aberta até limpar e regenerar o snapshot.** |
| Texto livre enviado à IA | [Gemini.cs](../server/Ceara.Api/Gemini.cs) envia a pergunta atual e até 12 mensagens de histórico após verificação local por [GeminiPrivacy.cs](../server/Ceara.Api/GeminiPrivacy.cs). Há nova verificação antes de cada chamada, incluindo metadados, textos, argumentos e respostas de ferramentas. As respostas das ferramentas não repetem filtros livres e omitem contagens de buscas com uma a quatro empresas e linhas de agregados com menos de cinco. Texto livre aprovado pelo filtro ainda pode identificar pessoas; não há garantia de anonimização. |
| Informação sobre o assistente | [assistant.component.html](../app/src/componentes/assistant.component.html), linha 2, e [base.component.ts](../app/src/componentes/base.component.ts), linha 12: os avisos agora informam envio do histórico e limites do filtro. Ainda é necessário um aviso de privacidade completo que trate das condições do provedor e das demais operações. |
| Requisições a mapas externos | [map.component.ts](../app/src/componentes/map.component.ts), linhas 58 e 127–143, e [map-style.ts](../app/src/lib/map-style.ts), linhas 14–16: o modo padrão carrega estilos e recursos de mapas externos pelo navegador. Incluir esse fluxo no inventário e no aviso aos visitantes; a alternativa com malhas locais já existe. A inspeção do código não confirma retenção ou uso pelo fornecedor. |
| Ausência de fluxo de direitos | Não foi localizado aviso de privacidade completo, identificação/canal do controlador, procedimento de correção/supressão nem política de retenção na interface/API/pipeline examinados. O comentário em [relatorio.py](../src/relatorio.py), linhas 248–251, recomenda respeitar descadastro, mas não implementa atendimento. |

Há medidas positivas: SQL parametrizado, base fora da pasta estática, validação de snapshot e segregação de registros individuais das respostas das ferramentas enviadas ao Gemini. Elas ajudam na segurança e minimização, mas não estabelecem por si a finalidade/base legal nem resolvem os direitos dos titulares.

### Filtro local de envio ao Gemini

Implementado após esta avaliação, a pedido do usuário. O servidor rejeita com HTTP 422 perguntas e histórico que coincidam com padrões comuns de CPF/CNPJ, e-mail, telefone, CEP, documentos rotulados, nomes explicitamente rotulados e endereços com número. Também verifica cada payload antes do transporte HTTP, preservando IDs e assinaturas opacas do provedor. A detecção normaliza caracteres invisíveis e dígitos Unicode; não transmite o texto a outra IA nem inclui o dado bloqueado no erro. Mensagens que falham não são acrescentadas ao histórico da interface.

Esse mecanismo não reconhece todos os nomes, endereços sem número, dados sensíveis em linguagem livre ou informações ofuscadas. Pode rejeitar também dados comerciais legítimos. A supressão de pequenos grupos é uma medida de minimização, não uma prova de anonimização nem um requisito numérico definido pela LGPD. O filtro não limpa o cadastro público, não resolve os CPFs remanescentes do snapshot e não estabelece conformidade com os termos da modalidade gratuita para qualquer texto aprovado.

Verificação da implementação: 74 testes da API passaram, incluindo bloqueio antes da primeira chamada, histórico de ambos os papéis, Unicode, metadados, argumentos, respostas de ferramentas e preservação das assinaturas. Passaram também três fluxos do assistente no navegador, a checagem de tipos Angular e o build de produção. Os testes de chat usam respostas simuladas; não foi enviada uma pergunta ao Gemini real nesta verificação.

## Próximos passos recomendados antes da abertura pública

1. Começar por mapas/estatísticas agregadas ou dados sintéticos enquanto se define a governança do diretório nominal. Agregação exige cuidado com recortes pequenos e possibilidade de reidentificação.
2. Mapear dados pessoais e finalidades; recuperar natureza jurídica/indicador MEI na preparação; revisar nomes/razões sociais com identificadores e contatos/residências de pessoas físicas. Remover o telefone só da tela não o remove da API.
3. Documentar a hipótese legal por finalidade e o balanceamento quando pertinente. Examinar necessidade de publicação de contato, WhatsApp e score. Política de privacidade e aviso de cookies não substituem essa avaliação.
4. Implantar aviso de privacidade, canal e fluxo verificável de acesso/correção/oposição/supressão, com registro proporcional das decisões e respeito nas atualizações. Avaliar encarregado e possíveis regras de pequeno porte no caso concreto.
5. Definir colunas autorizadas da API, limites de uso, controles contra extração em escala, retenção, logs e resposta a incidentes. Validar que snapshots antigos não ressuscitam dados suprimidos.
6. Confirmar regime/contratos do Gemini e as transferências da hospedagem, IA e demais integrações. Desativar o envio de texto livre à quota gratuita enquanto houver risco de dados pessoais; preferir minimização antes de qualquer envio.
7. Submeter o diretório e a fundamentação a revisão jurídica antes de oferecê-los amplamente para prospecção. A avaliação precisa dos contratos e do modo de operação real, além do código.

## Limites

Não foram examinados contratos privados, console da Vercel/Google, faturamento ativo, subprocessadores efetivamente usados, logs reais, destino de cada operação, prática comercial de usuários ou identidade de titulares. O projeto não foi publicado durante esta avaliação. Não há diagnóstico definitivo de infração ou previsão de sanção. As fontes foram consultadas em 5/10/2026 e condições de fornecedores podem mudar.
