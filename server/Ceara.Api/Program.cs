using System.Text.Json;
using System.Threading.RateLimiting;
using Ceara.Api;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
LocalEnvironment.Load(builder.Configuration);
var port = builder.Configuration["PORT"] ?? "8000";
builder.WebHost.UseUrls($"http://{(builder.Configuration["VERCEL"] == "1" || builder.Configuration["CEARA_CONTAINER"] == "1" ? "0.0.0.0" : "127.0.0.1")}:{port}");
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = WireJson.Options.PropertyNamingPolicy;
    options.SerializerOptions.PropertyNameCaseInsensitive = false;
});
builder.Services.AddOpenApi();
builder.Services.AddSingleton<Database>();
builder.Services.AddHttpClient<Gemini>(http => http.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddRateLimiter(options =>
{
    // Per-instance protection for local use. Vercel's WAF rule provides the
    // edge limit across instances; do not treat this as a distributed budget.
    options.AddPolicy("chat", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new() { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, cancellation) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new { detail = "Muitas perguntas em pouco tempo. Aguarde um minuto e tente novamente." }, cancellation);
    };
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    if (builder.Configuration["VERCEL"] != "1") return;
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedFor;
    // The deployed container is reachable only through the Vercel gateway.
    options.KnownIPNetworks.Clear(); options.KnownProxies.Clear();
    options.ForwardLimit = 1;
});

var app = builder.Build();
await Snapshot.Restore(app.Configuration, app.Services.GetRequiredService<Database>());
if (app.Configuration["VERCEL"] == "1") app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    try
    {
        if (context.Request.Path.StartsWithSegments("/api") && context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
        {
            var origin = context.Request.Headers.Origin.ToString();
            var ownOrigin = $"{context.Request.Scheme}://{context.Request.Host}";
            var devOrigins = app.Environment.IsDevelopment() && origin is ("http://localhost:3000" or "http://127.0.0.1:3000");
            if (origin.Length > 0 && origin != ownOrigin && !devOrigins) throw new ApiError("Origem não permitida.", 403);
            if (!context.Request.HasJsonContentType()) throw new ApiError("Envie application/json.", 415);
        }
        await next(context);
    }
    catch (ApiError error) { context.Response.StatusCode = error.Status; await context.Response.WriteAsJsonAsync(new { detail = error.Message }); }
    catch (SqliteException) { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { detail = Database.QueryFailure }); }
    catch (BadHttpRequestException) { context.Response.StatusCode = 422; await context.Response.WriteAsJsonAsync(new { detail = "Os dados enviados são inválidos. Confira os campos e tente novamente." }); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
});
app.UseRateLimiter();
app.MapOpenApi("/api/openapi.json");
app.MapGet("/api/docs", () => Results.Content("""
    <!doctype html><html lang="pt-BR"><meta charset="utf-8"><title>API — Ceará</title>
    <h1>Mapa de Oportunidades Ceará — API</h1><p><a href="/api/openapi.json">Especificação OpenAPI</a></p>
    </html>
    """, "text/html"));
app.MapGet("/api/saude", () => new { status = "ok", uf = "CE" });
app.MapGet("/api/base", (Database database, Gemini gemini, CancellationToken cancellation) =>
{
    var info = new Dictionary<string, object?> { ["ia_configurada"] = gemini.Configured, ["modelo_ia"] = gemini.Model };
    try { foreach (var pair in database.Metadata(cancellation)) info[pair.Key] = pair.Value; info["disponivel"] = true; }
    catch (DatabaseUnavailable error) { info["disponivel"] = false; info["erro"] = error.Message; }
    return info;
});
app.MapPost("/api/contatos/buscar", async (HttpRequest request, Database database, CancellationToken cancellation) =>
    database.Search(await Read<SearchRequest>(request, cancellation), cancellation)).Accepts<SearchRequest>("application/json").Produces<ContactPage>();
app.MapGet("/api/contatos/{id}", (string id, Database database, CancellationToken cancellation) =>
{
    if (!long.TryParse(id, out var number)) throw new ApiError("Identificador de empresa inválido.", 422);
    return database.Detail(number, cancellation) ?? throw new ApiError("Empresa não encontrada.", 404);
});
app.MapPost("/api/facetas", async (HttpRequest request, Database database, CancellationToken cancellation) =>
    database.Facets(await Read<Filters>(request, cancellation), cancellation)).Accepts<Filters>("application/json");
app.MapGet("/api/municipios", (Database database, CancellationToken cancellation) => database.Areas("municipios", cancellation));
app.MapGet("/api/ramos", (Database database, CancellationToken cancellation) => database.Areas("segmentos", cancellation));
app.MapPost("/api/analises", async (HttpRequest request, Database database, CancellationToken cancellation) =>
    database.Analyze(await Read<AnalysisRequest>(request, cancellation), cancellation)).Accepts<AnalysisRequest>("application/json");
app.MapGet("/api/malhas/{nome}", (string nome, Database database) =>
{
    if (nome is not ("malha_23.geojson" or "malha_br.geojson")) throw new ApiError("Malha não disponível. Apenas o Ceará pode ser consultado.", 404);
    var path = Path.Combine(database.DataDirectory, "ibge", nome);
    if (!File.Exists(path)) throw new ApiError("Malha ausente. Prepare uma nova versão da base para baixar a malha do IBGE.", 404);
    return Results.File(path, "application/geo+json");
});
app.MapPost("/api/chat", async (HttpRequest request, Gemini gemini, CancellationToken cancellation) =>
    await gemini.Chat(await Read<ChatRequest>(request, cancellation), cancellation)).Accepts<ChatRequest>("application/json").RequireRateLimiting("chat");
// Never let a missing /api route fall through to the Angular document.
app.Map("/api/{**path}", () => Results.NotFound(new { detail = "Recurso não encontrado." }));
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = context =>
{
    context.Context.Response.Headers.CacheControl = context.File.Name == "index.html" ? "no-cache" : "public, max-age=3600";
} });
if (Directory.Exists(Path.Combine(app.Environment.ContentRootPath, "wwwroot"))) app.MapFallbackToFile("index.html");
app.Run();

static async Task<T> Read<T>(HttpRequest request, CancellationToken cancellation) where T : ValidatedRequest
{
    using var reader = new StreamReader(request.Body);
    return WireJson.Read<T>(await reader.ReadToEndAsync(cancellation));
}

public partial class Program;
