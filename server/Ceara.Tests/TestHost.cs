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
        using var connection = new SqliteConnection($"Data Source={Path.Combine(path, "contatos.db")}");
        connection.Open(); using var command = connection.CreateCommand();
        command.CommandText = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.sql")); command.ExecuteNonQuery();
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
        using var connection = new SqliteConnection($"Data Source={Path.Combine(DirectoryPath, "uf", "CE", "contatos.db")}"); connection.Open();
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
