using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

using var client = new HttpClient { BaseAddress = new Uri(args.FirstOrDefault() ?? "http://127.0.0.1:8080"), Timeout = TimeSpan.FromSeconds(30) };
var checks = new List<string>();
void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException("Falha: " + name); checks.Add(name); }
async Task<JsonElement> Post(string path, object body)
{
    using var response = await client.PostAsJsonAsync(path, body); response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<JsonElement>();
}
var timer = Stopwatch.StartNew();
var health = await client.GetFromJsonAsync<JsonElement>("/api/saude"); Check(health.GetProperty("uf").GetString() == "CE", "saúde");
var metadata = await client.GetFromJsonAsync<JsonElement>("/api/base"); Check(metadata.GetProperty("disponivel").GetBoolean(), "base disponível");
var total = metadata.GetProperty("contatos").GetInt64();
var page = await Post("/api/contatos/buscar", new { por_pagina = 7 }); Check(page.GetProperty("total").GetInt64() == total && page.GetProperty("itens").GetArrayLength() == Math.Min(7, total), "busca e paginação");
var id = page.GetProperty("itens")[0].GetProperty("id").GetInt64();
var detail = await client.GetFromJsonAsync<JsonElement>("/api/contatos/" + id); Check(detail.GetProperty("id").GetInt64() == id && !detail.TryGetProperty("busca", out _), "detalhes");
var filtered = await Post("/api/contatos/buscar", new { filtros = new { cidade = "fortÁleza", somente_celular = true }, por_pagina = 7 });
Check(filtered.GetProperty("itens").EnumerateArray().All(r => r.GetProperty("cidade").GetString() == "Fortaleza" && r.GetProperty("tem_celular").GetBoolean()), "filtros combinados");
var analysis = await Post("/api/analises", new { }); Check(analysis[0].GetProperty("contatos").GetInt64() == total, "agregados");
var facets = await Post("/api/facetas", new { cidade = "Fortaleza" }); Check(facets.GetProperty("municipios").GetArrayLength() > 0, "facetas");
foreach (var route in new[] { "municipios", "ramos" }) Check((await client.GetFromJsonAsync<JsonElement>("/api/" + route)).GetArrayLength() > 0, route);
foreach (var file in new[] { "malha_23.geojson", "malha_br.geojson" }) Check((await client.GetFromJsonAsync<JsonElement>("/api/malhas/" + file)).GetProperty("type").GetString() == "FeatureCollection", file);
using var index = await client.GetAsync("/"); Check(index.Content.Headers.ContentType?.MediaType == "text/html", "Angular");
var html = await index.Content.ReadAsStringAsync();
foreach (Match asset in Regex.Matches(html, "(?:src|href)=\"([^\"]+\\.(?:js|css))\""))
    Check((await client.GetAsync(asset.Groups[1].Value)).IsSuccessStatusCode, "asset " + asset.Groups[1].Value);
foreach (var path in new[] { "/api/ausente", "/api/tarefas", "/uf/CE/contatos.db", "/snapshot.tar.br", "/snapshot.tar.gz" })
    Check((await client.GetAsync(path)).StatusCode == HttpStatusCode.NotFound, "404 " + path);
foreach (var path in new[] { "/api/exportar", "/api/tarefas" }) Check((await client.PostAsJsonAsync(path, new { })).StatusCode == HttpStatusCode.NotFound, "removido " + path);
using var own = new HttpRequestMessage(HttpMethod.Post, "/api/contatos/buscar") { Content = JsonContent.Create(new { }) };
own.Headers.Add("Origin", client.BaseAddress.GetLeftPart(UriPartial.Authority)); Check((await client.SendAsync(own)).IsSuccessStatusCode, "mesma origem");
using var foreign = new HttpRequestMessage(HttpMethod.Post, "/api/contatos/buscar") { Content = JsonContent.Create(new { }) };
foreign.Headers.Add("Origin", "https://external.example"); Check((await client.SendAsync(foreign)).StatusCode == HttpStatusCode.Forbidden, "origem externa bloqueada");
Console.WriteLine(JsonSerializer.Serialize(new { contatos = total, versao = metadata.GetProperty("versao_receita").GetString(), checks, elapsed_ms = timer.ElapsedMilliseconds }, new JsonSerializerOptions { WriteIndented = true }));
