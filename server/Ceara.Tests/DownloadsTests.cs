using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Ceara.Api;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ceara.Tests;

public sealed class DownloadsTests
{
    [Theory]
    [InlineData("UPDATE meta SET valor='SP' WHERE chave='uf'")]
    [InlineData("UPDATE meta SET valor='politica-antiga' WHERE chave='politica_publicacao'")]
    [InlineData("UPDATE municipios SET uf='SP',codigo_ibge='3550308'")]
    public void PublicSqliteDownloadRejectsOtherStatesAndOldPoliciesEvenWithMatchingHash(string sql)
    {
        using var host = new TestHost(); host.MakePublic();
        var catalogPath = Directory.GetFiles(Path.Combine(host.DirectoryPath,"uf","CE","downloads"),"catalogo.json",SearchOption.AllDirectories).Single();
        var items = JsonSerializer.Deserialize<Downloads.Item[]>(File.ReadAllText(catalogPath),WireJson.Options)!;
        var index = Array.FindIndex(items,item=>item.Id=="banco");
        var file = Path.Combine(Path.GetDirectoryName(catalogPath)!,items[index].Arquivo);
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={file};Pooling=False"))
        {
            conn.Open(); using var command=conn.CreateCommand(); command.CommandText=sql; command.ExecuteNonQuery();
        }
        using (var stream=File.OpenRead(file))
            items[index]=items[index] with { TamanhoBytes=stream.Length,Sha256=Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() };
        File.WriteAllText(catalogPath,JsonSerializer.Serialize(items,WireJson.Options));
        var db=new Database(new ConfigurationBuilder().AddInMemoryCollection(host.Settings).Build());
        Assert.Throws<DatabaseUnavailable>(()=>new Downloads(db).Validate());
    }

    [Fact]
    public void ReviewedGeographicIssueRemainsQueryableButIsExcludedFromTheMap()
    {
        using var host = new TestHost();
        var folder = Path.Combine(host.DirectoryPath,"uf","SC"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder,"contatos.db");
        File.Copy(Path.Combine(host.DirectoryPath,"uf","CE","contatos.db"),path);
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
        {
            conn.Open(); using var command = conn.CreateCommand();
            command.CommandText = """
                UPDATE meta SET valor='SC' WHERE chave='uf';
                UPDATE municipios SET uf='SC',codigo_ibge='4205407';
                UPDATE municipios SET cod_municipio='0403',nome='Acara',codigo_ibge='' WHERE nome='Fortaleza';
                UPDATE contatos SET cidade='Acara',cod_municipio='0403' WHERE cidade='Fortaleza';
                """;
            command.ExecuteNonQuery();
            var db = new Database(new ConfigurationBuilder().AddInMemoryCollection(host.Settings).Build()).ForUf("SC");
            Assert.Throws<DatabaseUnavailable>(() => db.ValidateSnapshot());
            command.CommandText = "INSERT INTO meta VALUES('municipios_sem_malha','1'),('contatos_sem_malha','3')"; command.ExecuteNonQuery();
            db.ValidateSnapshot();
            Assert.Equal(5L, db.Metadata()["contatos"]);
            Assert.Equal(3L, db.Metadata()["contatos_sem_malha"]);
            Assert.DoesNotContain(db.Areas("municipios"), row => row["nome"]?.ToString() == "Acara");
            command.CommandText = "UPDATE municipios SET nome='Outro município' WHERE cod_municipio='0403'"; command.ExecuteNonQuery();
            Assert.Throws<DatabaseUnavailable>(() => db.ValidateSnapshot());
        }
    }

    [Fact]
    public async Task FilteredExportIncludesRowsBeyondTheVisiblePage()
    {
        using var host = new TestHost();
        host.Sql("""
            WITH RECURSIVE rows(n) AS (VALUES(1) UNION ALL SELECT n+1 FROM rows WHERE n<120)
            INSERT INTO contatos(cnpj,nome,cidade,segmento,score)
            SELECT printf('%014d',100000+n),'Empresa de teste','Fortaleza','Padaria e confeitaria',75 FROM rows
            """);
        using var client = host.CreateClient();
        var response = await client.PostAsJsonAsync("/api/contatos/buscar", new { filtros = new { cidade="Fortaleza" }, por_pagina=2 });
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, page.GetProperty("itens").GetArrayLength());
        Assert.Equal(123, page.GetProperty("total").GetInt32());
        var export = await client.PostAsJsonAsync("/api/contatos/exportar", new { cidade="Fortaleza" });
        export.EnsureSuccessStatusCode();
        using var zipped = new GZipStream(await export.Content.ReadAsStreamAsync(), CompressionMode.Decompress);
        using var reader = new StreamReader(zipped);
        Assert.Equal(124, (await reader.ReadToEndAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task WholeAndFilteredDownloadsRespectTheDataset(bool publicSite)
    {
        using var host = new TestHost();
        if (publicSite) host.MakePublic(); else host.PrepareDownloads();
        using var client = host.CreateClient();
        var catalog = await client.GetFromJsonAsync<JsonElement>("/api/downloads");
        Assert.Equal(7, catalog.GetArrayLength());
        foreach (var item in catalog.EnumerateArray())
        {
            var response = await client.GetAsync(item.GetProperty("url").GetString()); response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal(item.GetProperty("tamanho_bytes").GetInt64(), bytes.LongLength);
            Assert.Equal(item.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
        var export = await client.PostAsJsonAsync("/api/contatos/exportar", new { cidade="Fortaleza" }); export.EnsureSuccessStatusCode();
        using var zipped = new GZipStream(await export.Content.ReadAsStreamAsync(), CompressionMode.Decompress);
        using var reader = new StreamReader(zipped); var csv = await reader.ReadToEndAsync();
        Assert.Equal(4, csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        if (publicSite)
        {
            Assert.DoesNotContain("email",csv); Assert.DoesNotContain("nome",csv); Assert.DoesNotContain("\"empresa\"",csv.Split('\n')[0]);
            Assert.DoesNotContain("busca",csv); Assert.DoesNotContain("Rua Um",csv); Assert.DoesNotContain("gmail",csv);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/contatos/exportar", new { somente_email=true })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/downloads?uf=SP")).StatusCode);
            var ufs = await client.GetFromJsonAsync<JsonElement>("/api/ufs"); Assert.Equal(1,ufs.GetArrayLength()); Assert.Equal("CE",ufs[0].GetProperty("uf").GetString());
        }
        else { Assert.Contains("email",csv); Assert.Contains("padaria@gmail.com",csv); }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/downloads/supressoes")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync("/api/downloads/banco?geracao=old")).StatusCode);
    }

    [Fact]
    public async Task StateSelectionIsIndependentForConcurrentRequests()
    {
        using var host = new TestHost();
        var folder = Path.Combine(host.DirectoryPath,"uf","SP"); Directory.CreateDirectory(folder);
        File.Copy(Path.Combine(host.DirectoryPath,"uf","CE","contatos.db"),Path.Combine(folder,"contatos.db"));
        using (var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(folder,"contatos.db")}"))
        {
            db.Open(); using var command=db.CreateCommand(); command.CommandText="UPDATE meta SET valor='SP' WHERE chave='uf'; UPDATE contatos SET cidade='São Paulo'; UPDATE municipios SET uf='SP',codigo_ibge='3550308'"; command.ExecuteNonQuery();
        }
        using var client=host.CreateClient();
        var results=await Task.WhenAll(client.GetFromJsonAsync<JsonElement>("/api/base?uf=CE"),client.GetFromJsonAsync<JsonElement>("/api/base?uf=SP"));
        Assert.Equal("CE",results[0].GetProperty("uf").GetString()); Assert.Equal("SP",results[1].GetProperty("uf").GetString());
        var sp=await client.PostAsJsonAsync("/api/contatos/buscar?uf=SP",new{}); var data=await sp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.All(data.GetProperty("itens").EnumerateArray(),row=>Assert.Equal("São Paulo",row.GetProperty("cidade").GetString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await client.GetAsync("/api/base?uf=XX")).StatusCode);
    }
}
