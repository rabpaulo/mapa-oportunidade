using System.Net;
using System.Text.Json;
using Ceara.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ceara.Tests;

public sealed class TestHost : WebApplicationFactory<Program>
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "ceara-test-" + Guid.NewGuid().ToString("N"));
    public Dictionary<string, string?> Settings { get; } = new() { ["GEMINI_API_KEY"] = "simulated-key", ["GEMINI_MODEL"] = "gemini-simulated", ["CEARA_CONTAINER"] = "1" };
    public Queue<Func<HttpRequestMessage, Task<HttpResponseMessage>>> Responses { get; } = new();
    public List<JsonElement> Requests { get; } = [];
    public TestHost()
    {
        Settings["CEARA_DATA_DIR"] = DirectoryPath;
        var path = Path.Combine(DirectoryPath, "uf", "CE"); Directory.CreateDirectory(path);
        using var connection = new SqliteConnection($"Pooling=False;Data Source={Path.Combine(path, "contatos.db")}");
        connection.Open(); using var command = connection.CreateCommand();
        command.CommandText = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.sql")); command.ExecuteNonQuery();
    }
    public void MakePublic()
    {
        Settings["CEARA_PUBLIC"] = "1";
        Settings["PUBLIC_RESPONSAVEL"] = "Responsável de Teste";
        Settings["PUBLIC_PRIVACY_EMAIL"] = "privacy@example.invalid";
        var path = Path.Combine(DirectoryPath,"uf","CE","contatos.db");
        File.Delete(path);
        using (var connection = new SqliteConnection($"Pooling=False;Data Source={path}"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","public.sql")); command.ExecuteNonQuery();
        }
        PrepareDownloads();
    }
    public void PrepareDownloads()
    {
        var path = Path.Combine(DirectoryPath,"uf","CE","contatos.db");
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("2026-09-14/2026-10-04 17:12/"))).ToLowerInvariant()[..24];
        var folder = Path.Combine(DirectoryPath,"uf","CE","downloads",key); Directory.CreateDirectory(folder);
        File.Copy(path, Path.Combine(folder,"contatos.db"), true);
        using var conn = new SqliteConnection($"Pooling=False;Data Source={path}"); conn.Open();
        var items = new List<Downloads.Item>();
        static string Hash(string file) { using var stream = File.OpenRead(file); return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant(); }
        void Add(string id, string file, string format, long rows) => items.Add(new(id,file,"CE","2026-09-14/2026-10-04 17:12",key,format,rows,new FileInfo(Path.Combine(folder,file)).Length,Hash(Path.Combine(folder,file))));
        foreach (var table in new[] { "contatos", "municipios", "segmentos" })
        {
            using var command = conn.CreateCommand(); command.CommandText = $"SELECT * FROM {table}"; using var reader = command.ExecuteReader();
            using var raw = File.Create(Path.Combine(folder,table+".csv.gz"));
            using var gzip = new System.IO.Compression.GZipStream(raw,System.IO.Compression.CompressionMode.Compress);
            using var writer = new StreamWriter(gzip); var rows = 0;
            var fields = Enumerable.Range(0, reader.FieldCount).Where(i => reader.GetName(i) != "busca").ToArray();
            writer.WriteLine(string.Join(',',fields.Select(reader.GetName)));
            while (reader.Read()) { writer.WriteLine(string.Join(',',fields.Select(i => Convert.ToString(reader.GetValue(i))))); rows++; }
            writer.Dispose(); gzip.Dispose(); raw.Dispose();
            Add(table,table+".csv.gz","csv.gz",rows);
        }
        Add("banco","contatos.db","sqlite",5);
        File.WriteAllText(Path.Combine(folder,"metadados.json"),"{}"); Add("metadados","metadados.json","json",1);
        Directory.CreateDirectory(Path.Combine(DirectoryPath,"ibge"));
        foreach (var (id,file) in new[] { ("malha","malha_23.geojson"), ("brasil","malha_br.geojson") })
        {
            File.WriteAllText(Path.Combine(folder,file),"{\"type\":\"FeatureCollection\",\"features\":[]}");
            File.Copy(Path.Combine(folder,file),Path.Combine(DirectoryPath,"ibge",file),true); Add(id,file,"geojson",0);
        }
        File.WriteAllText(Path.Combine(folder,"catalogo.json"), JsonSerializer.Serialize(items,WireJson.Options));
    }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Settings));
        builder.ConfigureServices(services => services.AddHttpClient<Gemini>().ConfigurePrimaryHttpMessageHandler(() => new FakeHandler(this)));
    }
    public void Respond(string parts, HttpStatusCode status = HttpStatusCode.OK) => Responses.Enqueue(_ => Task.FromResult(new HttpResponseMessage(status)
    { Content = new StringContent("{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":" + parts + "}}]}") }));
    public void Sql(string sql)
    {
        using var connection = new SqliteConnection($"Pooling=False;Data Source={Path.Combine(DirectoryPath, "uf", "CE", "contatos.db")}"); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
    }
    private sealed class FakeHandler(TestHost host) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host != "generativelanguage.googleapis.com") throw new InvalidOperationException("Unexpected provider");
            if (!request.Headers.TryGetValues("x-goog-api-key", out var keys) || keys.Single() != "simulated-key") throw new InvalidOperationException("Unexpected credential");
            host.Requests.Add(JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.Clone());
            return await host.Responses.Dequeue()(request);
        }
    }
}
