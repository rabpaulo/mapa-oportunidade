using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ceara.Api;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ceara.Tests;

public sealed class PublicPrivacyTests
{
    private static void MakePublic(TestHost host)
    {
        host.Settings["CEARA_PUBLIC"] = "1";
        host.Settings["PUBLIC_RESPONSAVEL"] = "Responsável de Teste";
        host.Settings["PUBLIC_PRIVACY_EMAIL"] = "privacy@example.invalid";
        host.Sql("""
            INSERT INTO meta VALUES('publicacao_restrita','1');
            UPDATE contatos SET email='', telefone='', whatsapp='', bairro='', endereco='', tem_celular=0, dominio_proprio=0;
            """);
    }

    [Fact]
    public async Task PublicResponsesUseAnAllowlistAndChatNeverCallsTheProvider()
    {
        using var host = new TestHost(); MakePublic(host); using var client = host.CreateClient();
        var response = await client.PostAsJsonAsync("/api/contatos/buscar", new { }); response.EnsureSuccessStatusCode();
        var row = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("itens")[0];
        Assert.Equal(10, row.EnumerateObject().Count());
        foreach (var field in new[] { "email", "telefone", "whatsapp", "endereco", "bairro", "busca", "tem_celular", "dominio_proprio" }) Assert.False(row.TryGetProperty(field, out _));
        var detail = await client.GetFromJsonAsync<JsonElement>("/api/contatos/1");
        Assert.False(detail.TryGetProperty("email", out _));
        var info = await client.GetFromJsonAsync<JsonElement>("/api/base");
        Assert.True(info.GetProperty("publicacao_restrita").GetBoolean());
        Assert.False(info.GetProperty("ia_configurada").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/chat", new { pergunta = "Compare" })).StatusCode);
        Assert.Empty(host.Requests);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains("connect-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public void OriginalOrMislabelledDatabaseCannotPassPublicValidation()
    {
        using var host = new TestHost(); host.Settings["CEARA_PUBLIC"] = "1";
        var db = new Database(new ConfigurationBuilder().AddInMemoryCollection(host.Settings).Build());
        Assert.Throws<DatabaseUnavailable>(() => db.ValidateSnapshot());
        host.Sql("INSERT INTO meta VALUES('publicacao_restrita','1')");
        Assert.Throws<DatabaseUnavailable>(() => db.ValidateSnapshot());
        host.Sql("UPDATE contatos SET email='',telefone='',whatsapp='',endereco='',bairro=''");
        db.ValidateSnapshot();
    }

    [Fact]
    public async Task PrivacyChannelAndRestrictedDimensionsAreAvailable()
    {
        using var host = new TestHost(); MakePublic(host); using var client = host.CreateClient();
        var policy = await client.GetFromJsonAsync<JsonElement>("/api/privacidade");
        Assert.Equal("privacy@example.invalid", policy.GetProperty("email").GetString());
        Assert.Equal("Responsável de Teste", policy.GetProperty("responsavel").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/analises", new { agrupar_por = new[] { "bairro" } })).StatusCode);
    }

    [Fact]
    public async Task FullPublicProfilePreservesEveryColumnWithoutDisablingHostingProtections()
    {
        using var host = new TestHost();
        host.Settings["CEARA_PUBLIC"] = "1";
        host.Settings["CEARA_DATA_PROFILE"] = "integral";
        host.Sql("UPDATE contatos SET nome='Empresa 123.456.789-09 Original', empresa='Empresa Original' WHERE id=1");
        using var client = host.CreateClient();
        var response = await client.PostAsJsonAsync("/api/contatos/buscar", new { });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5, result.GetProperty("total").GetInt32());
        var row = result.GetProperty("itens")[0];
        Assert.Equal(19, row.EnumerateObject().Count());
        Assert.Equal("Empresa 123.456.789-09 Original", row.GetProperty("nome").GetString());
        Assert.Equal("padaria@gmail.com", row.GetProperty("email").GetString());
        Assert.Equal("(85) 99999-0000", row.GetProperty("telefone").GetString());
        Assert.Equal("Rua Um 12", row.GetProperty("endereco").GetString());
        Assert.Equal("Aldeota", row.GetProperty("bairro").GetString());
        Assert.Equal("1389", row.GetProperty("cod_municipio").GetString());
        Assert.Equal(90, row.GetProperty("score").GetInt32());
        Assert.True(row.GetProperty("tem_celular").GetBoolean());
        Assert.False(row.GetProperty("dominio_proprio").GetBoolean());
        Assert.Contains("padaria", row.GetProperty("busca").GetString());
        Assert.Equal(row.ToString(), (await client.GetFromJsonAsync<JsonElement>("/api/contatos/1")).ToString());
        var info = await client.GetFromJsonAsync<JsonElement>("/api/base");
        Assert.Equal("integral", info.GetProperty("perfil_dados").GetString());
        Assert.True(info.GetProperty("hospedagem_publica").GetBoolean());
        Assert.False(info.GetProperty("publicacao_restrita").GetBoolean());
        Assert.False(info.GetProperty("ia_configurada").GetBoolean());
        var policy = await client.GetFromJsonAsync<JsonElement>("/api/privacidade");
        Assert.Contains("base local integral", policy.GetProperty("dados_publicados").GetString());
        Assert.Contains("ainda não foi configurado", policy.GetProperty("direitos").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/analises", new { agrupar_por = new[] { "bairro" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/chat", new { pergunta = "Compare" })).StatusCode);
        Assert.Empty(host.Requests);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains("connect-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public void FullPublicProfileRejectsAMinimizedSnapshot()
    {
        using var host = new TestHost(); MakePublic(host);
        host.Settings["CEARA_DATA_PROFILE"] = "integral";
        var db = new Database(new ConfigurationBuilder().AddInMemoryCollection(host.Settings).Build());
        Assert.Throws<DatabaseUnavailable>(() => db.ValidateSnapshot());
    }

    [Fact]
    public void InvalidProfileIsRejected()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CEARA_DATA_PROFILE"] = "typo" }).Build();
        Assert.Throws<InvalidOperationException>(() => PublicationProfile.Get(config));
    }
}
