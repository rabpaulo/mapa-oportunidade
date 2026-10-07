namespace Ceara.Api;

public sealed class PrivacyPolicy(IConfiguration config)
{
    public bool Restricted => PublicationProfile.Get(config) == PublicationProfile.Minimized;
    public bool PublicDeployment => PublicationProfile.IsPublic(config);
    public string Responsible => config["PUBLIC_RESPONSAVEL"]?.Trim() ?? "";
    public string Email => config["PUBLIC_PRIVACY_EMAIL"]?.Trim() ?? "";
    public object Notice => new
    {
        publicacao_restrita = Restricted, hospedagem_publica = PublicDeployment,
        perfil_dados = PublicationProfile.Get(config), responsavel = Responsible, email = Email,
        atualizado_em = "2026-10-07",
        finalidade = "Consulta de informações cadastrais empresariais e comparação de municípios e atividades da UF selecionada; na publicação pública, somente do Ceará.",
        dados_publicados = Restricted
            ? "CNPJ, município, atividade, porte, ano de abertura e score baseado em características empresariais. Empresários individuais e entidades sem classificação verificável são excluídos. Não são publicados nomes, razão social, nome fantasia, CPF, contatos, bairro ou endereço."
            : "Esta versão consulta a base local integral, sem omitir registros ou colunas: nomes, CNPJ, telefone, e-mail, WhatsApp, município, bairro, endereço, atividade, oportunidade, porte, ano de abertura, score e campos auxiliares. A base pode conter dados de pessoas físicas associados ao cadastro empresarial. Informações podem estar desatualizadas. O score é calculado pela aplicação e não é uma avaliação da Receita Federal.",
        visitantes = "O site não tem cadastro, publicidade, analytics ou formulários de coleta. A preferência de tema é guardada no seu navegador. A Vercel pode tratar IP, informações técnicas e registros de acesso para hospedagem e segurança.",
        compartilhamento = PublicDeployment
            ? "A versão pública usa a Vercel para hospedagem e malhas locais do IBGE. O assistente usa a chave do responsável, guardada no servidor, e envia perguntas, histórico, categorias e estatísticas ao Gemini. Registros individuais não são enviados ao provedor. Não inclua dados pessoais ou confidenciais."
            : "O banco é consultado pelo servidor local. Cada usuário configura sua própria chave do Gemini em .env. O assistente envia perguntas, histórico, categorias e estatísticas ao Gemini; não inclua dados pessoais ou confidenciais. O mapa de ruas pode consultar serviços externos.",
        retencao = "A versão publicada permanece até sua substituição. Correções ou supressões atendidas precisam ser aplicadas à cópia usada nos próximos releases e preservadas nas atualizações. Prazos dos registros técnicos da Vercel dependem dos serviços e configurações do provedor; não há promessa de eliminação imediata de todos os seus registros.",
        direitos = Email.Length > 0
            ? "Para solicitar acesso, correção, oposição, bloqueio ou supressão de dados pessoais, use o e-mail de atendimento. Informe o CNPJ, o campo e o pedido; não envie CPF completo ou documentos sem solicitação. Pedidos são avaliados conforme a LGPD e não alteram o cadastro oficial da Receita Federal."
            : "O canal de atendimento ainda não foi configurado. A configuração deve permitir pedidos de acesso, correção, oposição, bloqueio ou supressão de dados pessoais, conforme a LGPD. O site não altera o cadastro oficial da Receita Federal.",
        transferencias = "A Vercel e seus prestadores podem tratar dados técnicos em outros países. Hospedagem regional não garante residência integral no Brasil. Os mecanismos e contratos aplicáveis devem ser avaliados pelo responsável.",
        limites = "A origem pública dos dados não autoriza automaticamente qualquer reutilização ou prospecção. Executar uma cópia local também não dispensa obrigações aplicáveis ao seu uso. Este site não apresenta certificação de conformidade LGPD."
    };
}
