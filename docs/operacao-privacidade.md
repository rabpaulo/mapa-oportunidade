# Operação da publicação integral

A publicação usa uma cópia consistente da base local original, com 680.298 registros e 19 colunas, incluindo contatos e endereços. Não passa pelo minimizador, não exclui empresários individuais e não limpa nomes na preparação do snapshot. O banco original permanece intacto. Dados pessoais podem estar presentes nos campos do cadastro empresarial; não há certificação de conformidade LGPD.

## Configuração e publicação

Na Vercel, mantenha `CEARA_PUBLIC=1` e `CEARA_DATA_PROFILE=integral`. A primeira variável mantém CSP, mapa somente local, limites de requisições, redução de logs e bloqueio do Gemini; a segunda seleciona o banco completo. O perfil integral rejeita um snapshot marcado como minimizado para evitar publicar um recorte como se fosse completo.

```bash
.venv/bin/python scripts/preparar_release.py --dados data --saida releases/integral-NOVA-VERSAO --fixar
npx vercel deploy
# Depois de validar a prévia:
npx vercel promote URL_DO_DEPLOYMENT
```

O snapshot fixado é incluído no upload local. Bancos originais, releases e `.env` ficam fora do Git e do contexto enviado. O runtime consulta o banco somente para leitura, fora dos assets públicos. Ele não consulta registros externos durante as buscas.

O GitHub contém código, documentação e exemplos de configuração. Quem clonar baixa e prepara a fonte pública localmente conforme o README. Não inclua bases ou chaves em commits, Releases ou artifacts do GitHub. O arquivo `/codigo-fonte.tar.gz` oferece o código correspondente de cada deployment, sem dados e segredos, conforme a licença AGPL.

Deploys por push estão desativados em `vercel.json`: builds remotos apenas do Git não têm o snapshot. Os deploys continuam sendo feitos manualmente com a base fixada e validada.

## Aviso e atendimento

O site informa a origem: Dados Abertos CNPJ da Receita Federal, obtidos pelo espelho Casa dos Dados, com malhas e códigos do IBGE. Apresenta a versão do cadastro, esclarece que informações podem estar desatualizadas e identifica o score como cálculo da aplicação.

`PUBLIC_RESPONSAVEL` e `PUBLIC_PRIVACY_EMAIL` precisam de identificação real e endereço público monitorado. Ainda não foram fornecidos. A interface informa essa pendência e não inventa uma identidade nem um canal operacional.

Para pedidos de titulares, registre data, objeto e resultado em local privado. Solicite somente comprovação proporcional; não peça CPF completo ou documentos por padrão. Avalie acesso, correção, oposição, bloqueio e supressão conforme o direito aplicável. A declaração completa de acesso tem prazo legal de até 15 dias, sem generalizar esse prazo para todos os pedidos.

O perfil integral não aplica automaticamente a lista de supressões do minimizador. Uma correção ou retirada atendida precisa ser incorporada à cópia de publicação antes de gerar novos releases e preservada nas atualizações. Valide o resultado no banco, API e interface. Não edite apenas o SQLite temporário de um container.

Retire deployments anteriores afetados por pedidos atendidos e não faça rollback para uma base anterior ao atendimento. Revise também releases e cópias locais conforme finalidade e retenção. Não há promessa de apagar cópias de terceiros.

## Perfil minimizado opcional

`CEARA_DATA_PROFILE=minimizado` preserva as regras anteriores: exige base marcada, campos privados vazios e uma lista positiva de campos da API. A preparação exclui empresários individuais e classificações não verificadas, limpa identificadores nos nomes e reaplica a lista privada de supressões.

```bash
.venv/bin/python scripts/preparar_base_publica.py --saida artifacts/publica-NOVA-VERSAO
.venv/bin/python scripts/preparar_release.py --dados artifacts/publica-NOVA-VERSAO --saida releases/publica-NOVA-VERSAO --fixar
```

Esse fluxo não é usado pelo deployment integral.

## Pendências de governança

Defina finalidade, necessidade, hipótese legal dos dados pessoais, atendimento, retenção e controles de acesso. Confira operadores, contratos e mecanismos de transferência internacional aplicáveis à Vercel, inclusive registros técnicos. A pesquisa está em [fontes oficiais](lgpd-fontes-oficiais.md).

Em incidentes, contenha a exposição, preserve evidências com acesso restrito e avalie as obrigações de comunicação. A aplicação pública mantém o Gemini desativado, sem cadastro, analytics ou formulários de coleta. Na execução local, o filtro Gemini não garante anonimização de texto livre.
