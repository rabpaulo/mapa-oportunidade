using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ceara.Api;
using Xunit;

namespace Ceara.Tests;

public sealed class GeminiPrivacyTests
{
    [Theory]
    [InlineData("Meu CPF é 123.456.789-09")]
    [InlineData("Documento 12345678909")]
    [InlineData("Documento 123 456 789 09")]
    [InlineData("Documento １２３４５６７８９０９")]
    [InlineData("Documento ١٢٣٤٥٦٧٨٩٠٩")]
    [InlineData("Documento 123\u200b45678909")]
    [InlineData("Consulte 12.345.678/0001-99")]
    [InlineData("Contato pessoa@example.invalid")]
    [InlineData("Contato pessoa@exa\u200bmple.invalid")]
    [InlineData("Ligue (85) 99999-0000")]
    [InlineData("Ligue +55 85 99999-0000")]
    [InlineData("Ligue 85999990000")]
    [InlineData("Ligue 99999-0000")]
    [InlineData("Ligue 3333-4444")]
    [InlineData("RG: 1234567")]
    [InlineData("CEP 60123-456")]
    [InlineData("Moro na Rua Exemplo, número 12")]
    [InlineData("Av. Exemplo 20")]
    [InlineData("Meu nome é Pessoa de Teste")]
    [InlineData("Nome completo: Pessoa de Teste")]
    public async Task IdentifiersAreRejectedBeforeAnyProviderRequest(string question)
    {
        using var host = new TestHost(); using var client = host.CreateClient();
        var response = await client.PostAsJsonAsync("/api/chat", new { pergunta = question });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(GeminiPrivacy.RequestError, error.GetProperty("detail").GetString());
        Assert.Empty(host.Requests);
    }

    [Theory] [InlineData("user")] [InlineData("model")]
    public async Task UnsafeHistoryIsRejectedEvenWhenTheCurrentQuestionIsSafe(string role)
    {
        using var host = new TestHost(); using var client = host.CreateClient();
        var response = await client.PostAsJsonAsync("/api/chat", new { pergunta = "Compare os municípios", historico = new[] { new { role, text = "pessoa@example.invalid" } } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Empty(host.Requests);
    }

    [Theory]
    [InlineData("Compare Fortaleza e Sobral entre 2020-2025")]
    [InlineData("A base tem 680.298 empresas? Qual é o score médio?")]
    [InlineData("Explique CPF, CNPJ e a LGPD sem usar dados reais")]
    [InlineData("A versão do cadastro é 14/09/2026 e a geração é 04/10/2026")]
    [InlineData("Me ajude a planejar uma pesquisa de mercado")]
    public async Task GeneralQuestionsAndOrdinaryNumbersRemainUsable(string question)
    {
        using var host = new TestHost(); host.Respond("[{\"text\":\"Vamos analisar.\"}]"); using var client = host.CreateClient();
        (await client.PostAsJsonAsync("/api/chat", new { pergunta = question })).EnsureSuccessStatusCode();
        Assert.Single(host.Requests);
        Assert.Equal(question, host.Requests[0].GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task ProviderGeneratedIdentifiersAreNotEchoedOnTheNextRound()
    {
        using var host = new TestHost();
        host.Respond("[{\"functionCall\":{\"name\":\"buscar_empresas\",\"args\":{\"filtros\":{\"termo\":\"pessoa@example.invalid\"}}}}]");
        using var client = host.CreateClient();
        var response = await client.PostAsJsonAsync("/api/chat", new { pergunta = "Encontre padarias" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Single(host.Requests);
        Assert.DoesNotContain("pessoa@example.invalid", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnsafeAggregateLabelsCannotReachTheProvider()
    {
        using var host = new TestHost();
        host.Sql("UPDATE contatos SET bairro='pessoa@example.invalid'");
        host.Respond("[{\"functionCall\":{\"name\":\"analisar_base\",\"args\":{\"agrupar_por\":[\"bairro\"]}}}]");
        using var client = host.CreateClient();
        var response = await client.PostAsJsonAsync("/api/chat", new { pergunta = "Compare por bairro" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Single(host.Requests);
        Assert.DoesNotContain("pessoa@example.invalid", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnsafeMetadataIsRejectedBeforeTheFirstRequest()
    {
        using var host = new TestHost(); host.Sql("UPDATE segmentos SET nome='pessoa@example.invalid' WHERE nome='Metalurgia'");
        using var client = host.CreateClient();
        var response = await client.PostAsJsonAsync("/api/chat", new { pergunta = "Compare os ramos" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Empty(host.Requests);
    }

    [Fact]
    public async Task SmallAggregateGroupsStayLocalAndProviderSignaturesArePreserved()
    {
        using var host = new TestHost();
        host.Respond("[{\"thoughtSignature\":\"opaque-12345678909\",\"functionCall\":{\"id\":\"call-1\",\"name\":\"analisar_base\",\"args\":{\"agrupar_por\":[\"cidade\"]}}}]");
        host.Respond("[{\"text\":\"Consulte a análise na aplicação.\"}]");
        using var client = host.CreateClient();
        var response = await client.PostAsJsonAsync("/api/chat", new { pergunta = "Compare os municípios" }); response.EnsureSuccessStatusCode();
        Assert.Equal(3, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resultados")[0].GetProperty("itens").GetArrayLength());
        var outgoing = host.Requests[1].GetProperty("contents");
        Assert.Equal("opaque-12345678909", outgoing[1].GetProperty("parts")[0].GetProperty("thoughtSignature").GetString());
        var summary = outgoing[2].GetProperty("parts")[0].GetProperty("functionResponse").GetProperty("response");
        Assert.Empty(summary.GetProperty("itens").EnumerateArray());
        Assert.False(summary.TryGetProperty("filtros", out _));
    }

    [Fact]
    public async Task LargerSearchesKeepUsefulCountsWithoutRepeatingFreeTextFilters()
    {
        using var host = new TestHost();
        host.Respond("[{\"functionCall\":{\"name\":\"buscar_empresas\",\"args\":{\"limite\":1}}}]");
        host.Respond("[{\"text\":\"Consulte a tabela.\"}]");
        using var client = host.CreateClient();
        (await client.PostAsJsonAsync("/api/chat", new { pergunta = "Encontre empresas" })).EnsureSuccessStatusCode();
        var summary = host.Requests[1].GetProperty("contents")[2].GetProperty("parts")[0].GetProperty("functionResponse").GetProperty("response");
        Assert.Equal(5, summary.GetProperty("total").GetInt32());
        Assert.Equal(1, summary.GetProperty("exibidas").GetInt32());
        Assert.False(summary.TryGetProperty("filtros", out _));
        Assert.DoesNotContain("padaria@gmail.com", host.Requests[1].ToString());
    }
}
