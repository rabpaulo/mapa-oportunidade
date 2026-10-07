using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ceara.Api;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ceara.Tests;

public sealed class PublicPrivacyTests
{
    [Theory]
    [InlineData("00000000000191")]
    [InlineData("00000000E08G12")]
    public async Task PublicSearchPreservesLeadingZerosAndAlphanumericCnpjs(string cnpj)
    {
        using var host=new TestHost(); MakePublic(host);
        host.Sql($"UPDATE contatos SET cnpj='{cnpj}' WHERE id=1; INSERT INTO contatos_fts(contatos_fts) VALUES('rebuild')");
        host.PrepareDownloads();
        using var client=host.CreateClient();
        var response=await client.PostAsJsonAsync("/api/contatos/buscar",new { filtros=new { termo=cnpj.ToLowerInvariant() } });
        response.EnsureSuccessStatusCode();
        var result=await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1,result.GetProperty("total").GetInt32());
        Assert.Equal(cnpj,result.GetProperty("itens")[0].GetProperty("cnpj").GetString());
    }

    [Fact]
    public void PublicIndexCannotContainAnUnapprovedNameColumn()
    {
        using var host=new TestHost(); MakePublic(host);
        host.Sql("DROP TABLE contatos_fts; CREATE VIRTUAL TABLE contatos_fts USING fts5(nome,content='contatos',content_rowid='id')");
        var db=new Database(new ConfigurationBuilder().AddInMemoryCollection(host.Settings).Build());
        Assert.Throws<DatabaseUnavailable>(()=>db.ValidateSnapshot());
    }

    [Fact]
    public void PublicSnapshotRejectsAPersonalIdentifierInTheCnpjField()
    {
        using var host = new TestHost(); MakePublic(host);
        host.Sql("UPDATE contatos SET cnpj='12345678901' WHERE id=1");
        var db = new Database(new ConfigurationBuilder().AddInMemoryCollection(host.Settings).Build());
        Assert.Throws<DatabaseUnavailable>(() => db.ValidateSnapshot());
    }

    private static void MakePublic(TestHost host) => host.MakePublic();

    [Fact]
    public async Task PublicResponsesUseAnAllowlistAndChatUsesTheServerKey()
    {
        using var host = new TestHost(); MakePublic(host); using var client = host.CreateClient();
        var response = await client.PostAsJsonAsync("/api/contatos/buscar", new { }); response.EnsureSuccessStatusCode();
        var row = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("itens")[0];
        Assert.Equal(9, row.EnumerateObject().Count());
        foreach (var field in new[] { "nome", "empresa", "email", "telefone", "whatsapp", "endereco", "bairro", "busca", "tem_celular", "dominio_proprio" }) Assert.False(row.TryGetProperty(field, out _));
        var detail = await client.GetFromJsonAsync<JsonElement>("/api/contatos/1");
        Assert.False(detail.TryGetProperty("email", out _));
        var info = await client.GetFromJsonAsync<JsonElement>("/api/base");
        Assert.True(info.GetProperty("publicacao_restrita").GetBoolean());
        Assert.True(info.GetProperty("ia_configurada").GetBoolean());
        host.Respond("[{\"text\":\"Consulte os municípios do Ceará.\"}]");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/chat", new { pergunta = "Compare os municípios" })).StatusCode);
        Assert.Single(host.Requests);
        Assert.DoesNotContain("simulated-key", info.ToString());
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
        Assert.Throws<DatabaseUnavailable>(() => db.ValidateSnapshot());
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
    public void FullPublicProfileIsRejectedEvenWhenExplicitlyConfigured()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["CEARA_PUBLIC"]="1", ["CEARA_DATA_PROFILE"]="integral" }).Build();
        Assert.Throws<InvalidOperationException>(() => PublicationProfile.Get(config));
    }

    [Fact]
    public void FullPublicProfileRejectsAMinimizedSnapshot()
    {
        using var host = new TestHost(); MakePublic(host);
        host.Settings["CEARA_DATA_PROFILE"] = "integral";
        var db = new Database(new ConfigurationBuilder().AddInMemoryCollection(host.Settings).Build());
        Assert.Throws<InvalidOperationException>(() => db.ValidateSnapshot());
    }

    [Fact]
    public void InvalidProfileIsRejected()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CEARA_DATA_PROFILE"] = "typo" }).Build();
        Assert.Throws<InvalidOperationException>(() => PublicationProfile.Get(config));
    }
}
