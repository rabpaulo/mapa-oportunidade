using System.Text.Json;
using System.Threading.RateLimiting;
using Ceara.Api;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
LocalEnvironment.Load(builder.Configuration);
// The production image supplies this argument, independently of dashboard env.
if (args.Contains("--publicacao-ceara")) builder.Configuration["CEARA_PUBLIC"] = "1";
_ = PublicationProfile.Get(builder.Configuration);
// Request URLs can contain user-entered text; do not persist them in public logs.
if (PublicationProfile.IsPublic(builder.Configuration))
    builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
var port = builder.Configuration["PORT"] ?? "8000";
builder.WebHost.UseUrls($"http://{(builder.Configuration["VERCEL"] == "1" || builder.Configuration["CEARA_CONTAINER"] == "1" ? "0.0.0.0" : "127.0.0.1")}:{port}");
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = WireJson.Options.PropertyNamingPolicy;
    options.SerializerOptions.PropertyNameCaseInsensitive = false;
});
builder.Services.AddOpenApi();
builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<PrivacyPolicy>();
builder.Services.AddSingleton<Downloads>();
builder.Services.AddSingleton<ILocalCollector, PythonCollector>();
builder.Services.AddSingleton<LocalCollection>();
builder.Services.AddHttpClient<Gemini>(http => http.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        !PublicationProfile.IsPublic(builder.Configuration) ? RateLimitPartition.GetNoLimiter("local") :
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new() { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // Per-instance chat protection; this is not a distributed provider budget.
    options.AddPolicy("chat", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new() { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, cancellation) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new { detail = "Muitas solicitações em pouco tempo. Aguarde um minuto e tente novamente." }, cancellation);
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
if (app.Services.GetRequiredService<PrivacyPolicy>().PublicDeployment)
{
    app.Services.GetRequiredService<Database>().ValidateSnapshot();
    app.Services.GetRequiredService<Downloads>().Validate();
}
if (args.Contains("--validar-snapshot")) return;
if (app.Configuration["VERCEL"] == "1") app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    if (PublicationProfile.IsPublic(app.Configuration))
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; connect-src 'self'; worker-src 'self' blob:; font-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
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
app.MapGet("/api/privacidade", (PrivacyPolicy policy) => policy.Notice);
app.MapGet("/api/base", (string? uf, Database database, Gemini gemini, CancellationToken cancellation) =>
{
    var info = new Dictionary<string, object?> { ["ia_configurada"] = gemini.Configured, ["modelo_ia"] = gemini.Model };
    try { foreach (var pair in database.ForUf(uf).Metadata(cancellation)) info[pair.Key] = pair.Value; info["disponivel"] = true; }
    catch (DatabaseUnavailable error) { info["disponivel"] = false; info["erro"] = error.Message; }
    info["publicacao_restrita"] = app.Services.GetRequiredService<PrivacyPolicy>().Restricted;
    info["hospedagem_publica"] = app.Services.GetRequiredService<PrivacyPolicy>().PublicDeployment;
    info["perfil_dados"] = PublicationProfile.Get(app.Configuration);
    return info;
});
app.MapPost("/api/contatos/buscar", async (string? uf, HttpRequest request, Database database, CancellationToken cancellation) =>
    database.ForUf(uf).Search(await Read<SearchRequest>(request, cancellation), cancellation)).Accepts<SearchRequest>("application/json").Produces<ContactPage>();
app.MapGet("/api/contatos/{id}", (string id, string? uf, Database database, CancellationToken cancellation) =>
{
    if (!long.TryParse(id, out var number)) throw new ApiError("Identificador de empresa inválido.", 422);
    return database.ForUf(uf).Detail(number, cancellation) ?? throw new ApiError("Empresa não encontrada.", 404);
});
app.MapPost("/api/facetas", async (string? uf, HttpRequest request, Database database, CancellationToken cancellation) =>
    database.ForUf(uf).Facets(await Read<Filters>(request, cancellation), cancellation)).Accepts<Filters>("application/json");
app.MapGet("/api/municipios", (string? uf, Database database, CancellationToken cancellation) => database.ForUf(uf).Areas("municipios", cancellation));
app.MapGet("/api/ramos", (string? uf, Database database, CancellationToken cancellation) => database.ForUf(uf).Areas("segmentos", cancellation));
app.MapPost("/api/analises", async (string? uf, HttpRequest request, Database database, CancellationToken cancellation) =>
    database.ForUf(uf).Analyze(await Read<AnalysisRequest>(request, cancellation), cancellation)).Accepts<AnalysisRequest>("application/json");
app.MapGet("/api/malhas/{nome}", (string nome, string? uf, Database database) =>
{
    var selected = database.ForUf(uf);
    if (nome != $"malha_{Geography.Get(selected.Uf).Codigo}.geojson" && nome != "malha_br.geojson") throw new ApiError("Malha não disponível para a UF selecionada.", 404);
    var path = Path.Combine(database.DataDirectory, "ibge", nome);
    if (!File.Exists(path)) throw new ApiError("Malha ausente. Prepare uma nova versão da base para baixar a malha do IBGE.", 404);
    return Results.File(path, "application/geo+json");
});
app.MapPost("/api/chat", async (string? uf, HttpRequest request, Gemini gemini, CancellationToken cancellation) =>
    await gemini.Chat(await Read<ChatRequest>(request, cancellation), cancellation, uf)).Accepts<ChatRequest>("application/json").RequireRateLimiting("chat");
app.MapGet("/api/ufs", (Database database) => Geography.All
    .Where(s => !PublicationProfile.IsPublic(app.Configuration) || s.Uf == "CE")
    .Select(s => new { s.Uf, s.Nome, s.Codigo, s.TotalMunicipios, disponivel = File.Exists(database.ForUf(s.Uf).PathName) }));
app.MapGet("/api/coleta", (LocalCollection collection) => collection.Snapshot());
app.MapPost("/api/coleta", async (HttpRequest request, LocalCollection collection, CancellationToken cancellation) =>
    Results.Json(collection.Start(await Read<CollectionRequest>(request, cancellation)), WireJson.Options, statusCode: 202)).Accepts<CollectionRequest>("application/json");
app.MapGet("/api/downloads", (string? uf, Downloads downloads, CancellationToken cancellation) => downloads.Catalog(uf, cancellation));
app.MapGet("/api/downloads/{id}", (string id, string? uf, string? geracao, Downloads downloads, CancellationToken cancellation) => downloads.FileResult(id, uf, geracao, cancellation));
app.MapPost("/api/contatos/exportar", async (string? uf, HttpRequest request, Database database, CancellationToken cancellation) =>
{
    var selected = database.ForUf(uf);
    var filters = await Read<Filters>(request, cancellation);
    selected.ValidateFilters(filters);
    return Results.Stream(stream => selected.Export(filters, stream, cancellation), "application/gzip", $"{selected.Uf.ToLowerInvariant()}-recorte.csv.gz");
}).Accepts<Filters>("application/json");
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
