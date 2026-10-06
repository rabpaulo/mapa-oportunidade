using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Ceara.Tests;

public sealed class GeminiTests
{
    private static Task<HttpResponseMessage> Chat(HttpClient client) => client.PostAsJsonAsync("/api/chat", new { pergunta = "Encontre padarias" });
    [Fact] public async Task ToolCallsEchoSignaturesButNeverSendIndividualContacts()
    {
        using var host = new TestHost();
        host.Respond("[{\"thoughtSignature\":\"signature-1\",\"functionCall\":{\"id\":\"call-1\",\"name\":\"buscar_empresas\",\"args\":{\"filtros\":{\"termo\":\"padaria\"},\"limite\":2}}}]");
        host.Respond("[{\"text\":\"Consulte as empresas na tabela.\"}]");
        using var client = host.CreateClient(); var response = await Chat(client); response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(2, body.GetProperty("resultados")[0].GetProperty("itens").GetArrayLength());
        Assert.Equal("Padaria São José", body.GetProperty("resultados")[0].GetProperty("itens")[0].GetProperty("nome").GetString());
        Assert.Single(body.GetProperty("fontes").EnumerateArray());
        var outgoing = host.Requests[1].ToString();
        foreach (var value in new[] { "11111111000100", "Padaria São José", "padaria@gmail.com", "5585999990000", "Rua Um 12" }) Assert.DoesNotContain(value, outgoing);
        Assert.Contains("signature-1", outgoing); Assert.Contains("call-1", outgoing); Assert.Contains("exibidas", outgoing);
        var tool = host.Requests[1].GetProperty("contents")[2].GetProperty("parts")[0].GetProperty("functionResponse");
        Assert.Equal(JsonValueKind.Null, tool.GetProperty("response").GetProperty("total").ValueKind);
        Assert.False(tool.GetProperty("response").TryGetProperty("filtros", out _));
    }
    [Fact] public async Task AggregateToolsAndHistoryArePreserved()
    {
        using var host = new TestHost(); host.Respond("[{\"functionCall\":{\"name\":\"analisar_base\",\"args\":{\"agrupar_por\":[\"cidade\"]}}}]"); host.Respond("[{\"text\":\"Fortaleza lidera.\"}]");
        host.Sql("INSERT INTO contatos SELECT id+10, cnpj, nome, empresa, email, telefone, whatsapp, cidade, cod_municipio, bairro, endereco, segmento, oportunidade, porte, abertura, dominio_proprio, tem_celular, score, busca FROM contatos");
        using var client = host.CreateClient(); var history = Enumerable.Range(0, 20).Select(i => new { role = i % 2 == 0 ? "user" : "model", text = "message-" + i }).ToArray();
        var response = await client.PostAsJsonAsync("/api/chat", new { pergunta = "Compare", historico = history }); response.EnsureSuccessStatusCode();
        Assert.Equal(13, host.Requests[0].GetProperty("contents").GetArrayLength());
        Assert.Equal("message-8", host.Requests[0].GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Contains("percentual_com_email", host.Requests[1].ToString()); Assert.Equal("analise", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resultados")[0].GetProperty("tipo").GetString());
        var rows = host.Requests[1].GetProperty("contents")[14].GetProperty("parts")[0].GetProperty("functionResponse").GetProperty("response").GetProperty("itens");
        Assert.Single(rows.EnumerateArray());
        Assert.Equal(6, rows[0].GetProperty("contatos").GetInt32());
    }
    [Fact] public async Task InvalidToolsAreRecoverableAndRoundsAreBounded()
    {
        using var host = new TestHost();
        for (var i = 0; i < 5; i++) host.Respond("[{\"functionCall\":{\"name\":\"analisar_base\",\"args\":{\"agrupar_por\":[\"cnpj\"]}}}]");
        using var client = host.CreateClient(); Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Chat(client)).StatusCode);
        Assert.Equal(5, host.Requests.Count); Assert.Contains("Argumentos inv", host.Requests[1].ToString());
        Assert.Equal("NONE", host.Requests[4].GetProperty("toolConfig").GetProperty("functionCallingConfig").GetProperty("mode").GetString());
    }
    [Theory] [InlineData(401, 401)] [InlineData(403, 401)] [InlineData(429, 429)] [InlineData(404, 503)] [InlineData(500, 503)]
    public async Task ProviderFailuresAreSanitized(int provider, int expected)
    {
        using var host = new TestHost(); host.Respond("[]", (HttpStatusCode)provider); using var client = host.CreateClient();
        var response = await Chat(client); Assert.Equal(expected, (int)response.StatusCode); Assert.DoesNotContain("simulated-key", await response.Content.ReadAsStringAsync());
    }
    [Fact] public async Task TimeoutsAndMalformedResponsesAreClear()
    {
        using var host = new TestHost(); host.Responses.Enqueue(_ => throw new TaskCanceledException()); using var client = host.CreateClient(); Assert.Equal(HttpStatusCode.GatewayTimeout, (await Chat(client)).StatusCode);
        host.Responses.Enqueue(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("invalid json") })); Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Chat(client)).StatusCode);
        host.Respond("[{\"text\":\"private reasoning\",\"thought\":true}]"); Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Chat(client)).StatusCode);
        host.Respond("[{\"text\":45}]"); Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Chat(client)).StatusCode);
    }
    [Fact] public async Task MissingCredentialsAndRequestQuotaDoNotAffectBrowsing()
    {
        using var host = new TestHost(); host.Settings["GEMINI_API_KEY"] = ""; using var client = host.CreateClient();
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Chat(client)).StatusCode);
        var limited = await Chat(client); Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode); Assert.Equal("60", limited.Headers.RetryAfter?.ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/contatos/buscar", new { })).StatusCode);
        Assert.Empty(host.Requests);
    }
}
