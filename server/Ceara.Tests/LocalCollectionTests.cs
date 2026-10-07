using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ceara.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ceara.Tests;

public sealed class LocalCollectionTests
{
    private sealed class Collector : ILocalCollector
    {
        public TaskCompletionSource<int> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CollectionRequest? Request { get; private set; }
        public Task<int> Run(CollectionRequest request, Action<string> log, CancellationToken cancellation)
        {
            Request = request; log("Preparando " + string.Join(", ", request.Ufs));
            return Completion.Task.WaitAsync(cancellation);
        }
    }

    [Fact]
    public async Task LocalCollectionAcceptsMultipleStatesAndRejectsConcurrentJobs()
    {
        using var host = new TestHost(); var collector = new Collector();
        using var configured = host.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<ILocalCollector>(collector)));
        using var client = configured.CreateClient();
        var response = await client.PostAsJsonAsync("/api/coleta", new { ufs = new[] { "SP", "AC", "SP" }, reaproveitar = true });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/coleta", new { ufs = new[] { "CE" } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/contatos/buscar", new { })).StatusCode);
        collector.Completion.SetResult(0);
        var state = await AwaitCompletion(client);
        Assert.Equal("concluido", state.GetProperty("status").GetString());
        Assert.Equal(new[] { "SP", "AC" }, collector.Request!.Ufs); Assert.True(collector.Request.Reaproveitar);
        Assert.Contains("Preparando", state.GetProperty("linhas")[0].GetString());
    }

    [Theory]
    [InlineData("{\"ufs\":[]}")]
    [InlineData("{\"ufs\":[\"XX\"]}")]
    [InlineData("{\"ufs\":[\"../../etc\"]}")]
    [InlineData("{\"ufs\":null}")]
    [InlineData("{\"ufs\":[null]}")]
    public async Task InvalidStatesCannotStartAProcess(string json)
    {
        using var host = new TestHost(); using var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsync("/api/coleta", new StringContent(json, System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal("ocioso", (await client.GetFromJsonAsync<JsonElement>("/api/coleta")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task FailedCollectionKeepsTheExistingDatabaseAvailable()
    {
        using var host = new TestHost(); var collector = new Collector(); collector.Completion.SetResult(1);
        using var configured = host.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<ILocalCollector>(collector)));
        using var client = configured.CreateClient();
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/coleta", new { ufs = new[] { "TO" } })).StatusCode);
        Assert.Equal("erro", (await AwaitCompletion(client)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/contatos/buscar", new { })).StatusCode);
    }

    [Fact]
    public async Task PublicDeploymentCannotCollectAnyState()
    {
        using var host = new TestHost(); host.MakePublic(); using var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/coleta")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/coleta", new { ufs = new[] { "CE", "SP" } })).StatusCode);
    }

    private static async Task<JsonElement> AwaitCompletion(HttpClient client)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var state = await client.GetFromJsonAsync<JsonElement>("/api/coleta");
            if (state.GetProperty("status").GetString() != "rodando") return state;
            await Task.Delay(10);
        }
        throw new TimeoutException("A coleta de teste não terminou.");
    }
}
