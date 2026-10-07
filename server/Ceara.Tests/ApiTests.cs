using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ceara.Api;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ceara.Tests;

public sealed class ApiTests
{
    private static async Task<JsonElement> Post(HttpClient client, string route, object body)
    {
        var response = await client.PostAsJsonAsync("/api/" + route, body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    [Fact] public async Task SearchPreservesAccentsPrefixAndAllColumns()
    {
        using var host = new TestHost(); using var client = host.CreateClient();
        var found = await Post(client, "contatos/buscar", new { filtros = new { termo = "sao jose" } });
        Assert.Equal(1, found.GetProperty("total").GetInt32());
        var row = found.GetProperty("itens")[0]; Assert.Equal("Padaria São José", row.GetProperty("nome").GetString());
        Assert.Contains("padaria sao jose", row.GetProperty("busca").GetString()); Assert.True(row.GetProperty("tem_celular").GetBoolean());
        found = await Post(client, "contatos/buscar", new { filtros = new { termo = "padar" } }); Assert.Equal(3, found.GetProperty("total").GetInt32());
        found = await Post(client, "contatos/buscar", new { filtros = new { termo = "*** OR \"'" } }); Assert.Equal(0, found.GetProperty("total").GetInt32());
    }
    [Fact] public async Task CombinedAndListFiltersAreCanonicalAndParameterized()
    {
        using var host = new TestHost(); using var client = host.CreateClient();
        var found = await Post(client, "contatos/buscar", new { filtros = new { cidade = "fortÁleza", segmento = "PADARIA E CONFEITARIA", bairro = "aldeota", porte = "microempresa", ano_minimo = 2018, ano_maximo = 2020, score_minimo = 80, somente_celular = true, somente_email = true, somente_sem_dominio = true } });
        Assert.Equal(1, found.GetProperty("total").GetInt32());
        found = await Post(client, "contatos/buscar", new { filtros = new { cidades = new[] { "fortaleza", "juazeiro do norte" }, segmentos = new[] { "padaria e confeitaria" }, somente_celular = true } });
        Assert.Equal(3, found.GetProperty("total").GetInt32());
        found = await Post(client, "contatos/buscar", new { filtros = new { cidade = "' OR 1=1 --" } }); Assert.Equal(0, found.GetProperty("total").GetInt32());
    }
    [Theory]
    [InlineData("score", 1)] [InlineData("nome", 2)] [InlineData("cidade", 1)] [InlineData("recente", 5)] [InlineData("antiga", 1)]
    public async Task OrderingAndPagination(string order, int first)
    {
        using var host = new TestHost(); using var client = host.CreateClient();
        var page = await Post(client, "contatos/buscar", new { filtros = new { ordem = order }, pagina = 1, por_pagina = 2 });
        Assert.Equal(5, page.GetProperty("total").GetInt32()); Assert.Equal(2, page.GetProperty("itens").GetArrayLength()); Assert.Equal(first, page.GetProperty("itens")[0].GetProperty("id").GetInt32());
        var next = await Post(client, "contatos/buscar", new { filtros = new { ordem = order }, pagina = 2, por_pagina = 2 });
        Assert.DoesNotContain(next.GetProperty("itens").EnumerateArray(), row => row.GetProperty("id").GetInt32() == first);
        page = await Post(client, "contatos/buscar", new { pagina = 1000000 }); Assert.Equal(0, page.GetProperty("itens").GetArrayLength());
    }
    [Fact] public async Task FacetsAndAggregatesMatchTheFilteredPopulation()
    {
        using var host = new TestHost(); using var client = host.CreateClient();
        var rows = await Post(client, "analises", new { agrupar_por = new[] { "cidade" } });
        var city = rows[0]; Assert.Equal("Fortaleza", city.GetProperty("cidade").GetString());
        Assert.Equal(3, city.GetProperty("contatos").GetInt32()); Assert.Equal(66.67, city.GetProperty("percentual_com_celular").GetDouble()); Assert.Equal(70, city.GetProperty("score_medio").GetDouble());
        var total = (await Post(client, "analises", new { filtros = new { termo = "padaria" } }))[0]; Assert.Equal(3, total.GetProperty("contatos").GetInt32());
        var facets = await Post(client, "facetas", new { cidade = "Fortaleza", segmento = "Padaria e confeitaria" });
        Assert.Equal(2, facets.GetProperty("municipios").GetArrayLength()); Assert.Equal(2, facets.GetProperty("segmentos").GetArrayLength());
        var empty = (await Post(client, "analises", new { filtros = new { cidade = "Ausente" } }))[0]; Assert.Equal(0, empty.GetProperty("com_celular").GetInt32());
        Assert.Equal(0, empty.GetProperty("percentual_com_email").GetInt32());
    }
    [Fact] public async Task MetadataAreasDetailAndGeometry()
    {
        using var host = new TestHost(); using var client = host.CreateClient();
        var info = await client.GetFromJsonAsync<JsonElement>("/api/base");
        Assert.True(info.GetProperty("disponivel").GetBoolean()); Assert.Equal(5, info.GetProperty("contatos").GetInt32()); Assert.Equal(3, info.GetProperty("municipios").GetInt32());
        Assert.Equal("2026-09-14", info.GetProperty("versao_receita").GetString());
        var areas = await client.GetFromJsonAsync<JsonElement>("/api/municipios"); Assert.Equal("2304400", areas[0].GetProperty("codigo").GetString());
        Assert.Equal(3, (await client.GetFromJsonAsync<JsonElement>("/api/ramos")).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/contatos/99")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/malhas/malha_42.geojson")).StatusCode);
        Directory.CreateDirectory(Path.Combine(host.DirectoryPath, "ibge")); await File.WriteAllTextAsync(Path.Combine(host.DirectoryPath, "ibge", "malha_23.geojson"), "{\"type\":\"FeatureCollection\",\"features\":[]}");
        var geo = await client.GetAsync("/api/malhas/malha_23.geojson"); Assert.Equal("application/geo+json", geo.Content.Headers.ContentType?.MediaType);
    }
    [Fact] public async Task FacetsIncludeEveryMunicipalityWithAndWithoutContactFilters()
    {
        using var host = new TestHost();
        host.Sql("""
            WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n WHERE x<201)
            INSERT INTO contatos(cnpj,nome,cidade,cod_municipio,segmento,score,email,tem_celular,dominio_proprio)
            SELECT printf('%014d',x),'Empresa',printf('Cidade %03d',x),printf('%04d',x),'Ramo de teste',50,'teste@example.invalid',0,1 FROM n;
            INSERT INTO municipios SELECT cod_municipio,'2300000',cidade,'CE',COUNT(*),COUNT(*),0,0,50 FROM contatos WHERE segmento='Ramo de teste' GROUP BY cidade;
            """);
        using var client = host.CreateClient();
        var all = await Post(client,"facetas",new{});
        Assert.Equal(204,all.GetProperty("municipios").GetArrayLength());
        Assert.Equal(100,all.GetProperty("municipios")[0].GetProperty("percentual_com_email").GetInt32());
        var filtered = await Post(client,"facetas",new { somente_email=true });
        Assert.Equal(203,filtered.GetProperty("municipios").GetArrayLength());
        Assert.Contains(filtered.GetProperty("municipios").EnumerateArray(),row=>row.GetProperty("nome").GetString()=="Cidade 201");
    }
    [Theory] [InlineData("missing")] [InlineData("foreign")] [InlineData("broken")]
    public async Task MissingAndInvalidDatabaseFailClearly(string mode)
    {
        using var host = new TestHost();
        if (mode == "missing") File.Delete(Path.Combine(host.DirectoryPath, "uf", "CE", "contatos.db"));
        if (mode == "foreign") host.Sql("UPDATE meta SET valor='SC' WHERE chave='uf'");
        if (mode == "broken") host.Sql("DROP TABLE contatos_fts");
        using var client = host.CreateClient();
        var response = await client.PostAsJsonAsync("/api/contatos/buscar", new { filtros = new { termo = "padaria" } });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString()?.Length > 0);
        if (mode != "broken") Assert.False((await client.GetFromJsonAsync<JsonElement>("/api/base")).GetProperty("disponivel").GetBoolean());
    }
    [Theory]
    [InlineData("{\"pagina\":0}")] [InlineData("{\"por_pagina\":201}")] [InlineData("{\"filtros\":{\"score_minimo\":101}}")]
    [InlineData("{\"filtros\":{\"ano_minimo\":2025,\"ano_maximo\":2020}}")]
    [InlineData("{\"filtros\":{\"ordem\":\"DROP TABLE contatos\"}}")]
    [InlineData("{\"extra\":1}")] [InlineData("{\"filtros\":null}")] [InlineData("{\"filtros\":{\"cidades\":[null]}}")]
    public async Task InvalidContractsAreRejected(string json)
    {
        using var host = new TestHost(); using var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsync("/api/contatos/buscar", new StringContent(json, Encoding.UTF8, "application/json"))).StatusCode);
    }
    [Fact] public async Task PublicHostsRequireSameOriginAndRemovedRoutesStayRemoved()
    {
        using var host = new TestHost(); using var client = host.CreateClient(new() { BaseAddress = new Uri("https://project-preview.vercel.app") });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/contatos/buscar") { Content = JsonContent.Create(new { }) };
        request.Headers.Add("Origin", "https://project-preview.vercel.app"); Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
        using var external = new HttpRequestMessage(HttpMethod.Post, "/api/contatos/buscar") { Content = JsonContent.Create(new { }) };
        external.Headers.Add("Origin", "https://external.example"); Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(external)).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.PostAsync("/api/contatos/buscar", new StringContent("{}"))).StatusCode);
        foreach (var route in new[] { "exportar", "tarefas" }) Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/" + route, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/tarefas")).StatusCode);
    }
    [Fact] public async Task VercelPreviewUsesTheForwardedHttpsOrigin()
    {
        using var host = new TestHost(); host.MakePublic(); host.Settings["VERCEL"] = "1";
        using var client = host.CreateClient(new() { BaseAddress = new Uri("http://internal:8080") });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/contatos/buscar") { Content = JsonContent.Create(new { }) };
        request.Headers.Add("Origin", "https://preview.vercel.app");
        request.Headers.Add("X-Forwarded-Host", "preview.vercel.app"); request.Headers.Add("X-Forwarded-Proto", "https"); request.Headers.Add("X-Forwarded-For", "203.0.113.20");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
    }
    [Fact] public void ReadOnlyQueriesLeaveTheDatabaseUnchanged()
    {
        using var host = new TestHost(); var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(host.Settings).Build();
        var db = new Database(config); var original = File.ReadAllBytes(db.PathName);
        db.ValidateSnapshot(); db.Search(new()); db.Analyze(new()); db.Facets(new());
        Assert.Equal(original, File.ReadAllBytes(db.PathName));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => db.Search(new(), cancellation.Token));
    }
    [Fact] public void CancellationInterruptsAQueryAlreadyRunning()
    {
        using var host = new TestHost();
        host.Sql("""
            WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n WHERE x<100000)
            INSERT INTO contatos(nome, cidade, bairro, score, email, tem_celular, dominio_proprio)
            SELECT 'Empresa', 'Fortaleza', printf('%s', 'Áçêntuação repetida para testar o prazo de consulta'), 50, '', 0, 0 FROM n;
            """);
        var db = new Database(new ConfigurationBuilder().AddInMemoryCollection(host.Settings).Build());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));
        var failure = Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => db.Search(new() { Filtros = new() { Bairro = "Ausente" } }, cancellation.Token));
        Assert.Equal(9, failure.SqliteErrorCode);
    }
}
