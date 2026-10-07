using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Ceara.Api;

public sealed partial class Gemini(HttpClient http, Database database, IConfiguration config)
{
    public const string DefaultModel = "gemini-3.5-flash-lite";
    public bool Configured => !string.IsNullOrWhiteSpace(config["GEMINI_API_KEY"]);
    public string Model => config["GEMINI_MODEL"]?.Trim() ?? DefaultModel;
    private static readonly string Prompt = Resource("Gemini.prompt.txt");
    private static readonly JsonNode Tools = JsonNode.Parse(Resource("Gemini.tools.json"))!;
    [GeneratedRegex("^[a-zA-Z0-9._-]+$")] private static partial Regex ModelId();

    private static string Resource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Ceara.Api." + name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    private static JsonNode? Node(object? value) => JsonSerializer.SerializeToNode(value, WireJson.Options);

    private static JsonNode ToolSchema(bool restricted)
    {
        var tools = Tools.DeepClone();
        if (!restricted) return tools;
        foreach (var declaration in tools[0]!["functionDeclarations"]!.AsArray())
        {
            var parameters = declaration!["parameters"]!;
            var filters = parameters["properties"]!["filtros"]!["properties"]!.AsObject();
            foreach (var field in new[] { "bairro", "somente_celular", "somente_email", "somente_sem_dominio" }) filters.Remove(field);
            filters["termo"]!["description"] = "Busca por prefixos em CNPJ, município e ramo, sem nomes ou contatos.";
            filters["ordem"]!["enum"] = new JsonArray("score", "cnpj", "cidade", "recente", "antiga");
            if (declaration["name"]!.GetValue<string>() == "buscar_empresas")
                declaration["description"] = "Encontra empresas do Ceará e exibe os campos públicos na aplicação. A IA recebe apenas contagens; recortes com menos de cinco empresas omitem essas contagens.";
            else
            {
                declaration["description"] = "Calcula contagens de empresas e médias de score, sem indicadores de contato. A IA não recebe grupos com menos de cinco empresas.";
                parameters["properties"]!["agrupar_por"]!["items"]!["enum"] = new JsonArray("cidade", "segmento", "porte", "abertura");
                parameters["properties"]!["ordenar_por"]!["enum"] = new JsonArray("contatos", "score_medio");
            }
        }
        return tools;
    }

    private async Task<JsonObject> Generate(JsonObject payload, CancellationToken cancellation)
    {
        GeminiPrivacy.EnsureSafePayload(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent");
        request.Headers.Add("x-goog-api-key", config["GEMINI_API_KEY"]!.Trim());
        request.Content = JsonContent.Create(payload);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var status = (int)response.StatusCode;
            if (status is 401 or 403) throw new ApiError("A chave do Gemini foi recusada. Confira GEMINI_API_KEY na configuração do servidor.", 401);
            if (status == 429) throw new ApiError("A cota gratuita do Gemini foi atingida. Aguarde e tente novamente.", 429);
            if (status == 404) throw new ApiError("O modelo configurado não está disponível. Confira GEMINI_MODEL na configuração do servidor.");
            if (status >= 400) throw new ApiError("O Gemini não conseguiu processar a pergunta. Tente reformulá-la.");
            var body = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
            var content = (body?["candidates"] as JsonArray)?.FirstOrDefault()?["content"] as JsonObject;
            if (content?["parts"] is not JsonArray { Count: > 0 }) throw new ApiError("O Gemini não retornou uma resposta para esta pergunta. Tente reformulá-la.");
            foreach (var part in (JsonArray)content["parts"]!)
            {
                if (part is not JsonObject p
                    || (p["text"] is not null && (p["text"] is not JsonValue text || !text.TryGetValue<string>(out _)))
                    || (p["thought"] is not null && (p["thought"] is not JsonValue thought || !thought.TryGetValue<bool>(out _)))
                    || (p["functionCall"] is not null && (p["functionCall"] is not JsonObject call || call["name"] is not JsonValue name || !name.TryGetValue<string>(out _))))
                    throw new ApiError("O Gemini retornou uma resposta inválida. Tente novamente.");
            }
            return content;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new ApiError("O Gemini demorou a responder. Tente novamente.", 504); }
        catch (HttpRequestException) { throw new ApiError("Não foi possível conectar ao Gemini. Verifique sua conexão."); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentOutOfRangeException)
        { throw new ApiError("O Gemini retornou uma resposta inválida. Tente novamente."); }
    }

    public async Task<JsonObject> Chat(ChatRequest request, CancellationToken cancellation, string? uf = null)
    {
        request.Validate();
        var selectedDatabase = database.ForUf(uf);
        GeminiPrivacy.EnsureSafeRequest(request);
        if (!Configured) throw new ApiError(PublicationProfile.IsPublic(config)
            ? "O assistente está temporariamente indisponível. O responsável precisa configurar o Gemini no servidor."
            : "Configure sua própria GEMINI_API_KEY no arquivo .env e reinicie a aplicação para usar o assistente.");
        if (!ModelId().IsMatch(Model)) throw new ApiError("GEMINI_MODEL possui um identificador inválido.");
        JsonNode context = new JsonObject { ["disponivel"] = false };
        Dictionary<string, object?>? metadata = null;
        try
        {
            metadata = selectedDatabase.Metadata(cancellation);
            context = Node(new
            {
                disponivel = true, uf = selectedDatabase.Uf, versao_receita = metadata["versao_receita"], contatos = metadata["contatos"],
                municipios = selectedDatabase.Areas("municipios", cancellation).Select(m => m["nome"]),
                ramos = selectedDatabase.Areas("segmentos", cancellation).Select(r => r["nome"]),
                portes = new[] { "Nao informado", "Microempresa", "Pequeno porte", "Medio/grande" }
            })!;
        }
        catch (DatabaseUnavailable) { }
        var contents = new JsonArray();
        foreach (var message in request.Historico.TakeLast(12)) contents.Add(Message(message.Role, message.Text));
        contents.Add(Message("user", request.Pergunta));
        var results = new JsonArray();
        var sources = new JsonArray();
        for (var turn = 0; turn < 5; turn++)
        {
            var payload = new JsonObject
            {
                ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = Prompt.Replace("Ceará", Geography.Get(selectedDatabase.Uf).Nome)
                    + (selectedDatabase.Restricted ? "Esta publicação só consulta o Ceará. Nomes, contatos, bairro e endereço não existem na base pública. O score público usa atividade, porte e ano de abertura. Use apenas os filtros e métricas do esquema de ferramentas disponível.\n" : "")
                    + context.ToJsonString() }) },
                ["contents"] = contents.DeepClone(), ["tools"] = ToolSchema(selectedDatabase.Restricted),
                ["generationConfig"] = new JsonObject { ["temperature"] = 0.3, ["maxOutputTokens"] = 2500 }
            };
            if (turn == 4) payload["toolConfig"] = new JsonObject { ["functionCallingConfig"] = new JsonObject { ["mode"] = "NONE" } };
            var content = await Generate(payload, cancellation);
            var parts = (JsonArray)content["parts"]!;
            var calls = parts.OfType<JsonObject>().Where(p => p["functionCall"] is not null).ToArray();
            if (calls.Length == 0)
            {
                var text = string.Join('\n', parts.OfType<JsonObject>()
                    .Where(p => p["text"] is JsonValue && p["thought"]?.GetValue<bool>() != true).Select(p => p["text"]!.GetValue<string>()));
                if (string.IsNullOrWhiteSpace(text)) throw new ApiError("O Gemini não concluiu a resposta. Tente novamente.");
                return new JsonObject { ["texto"] = text, ["resultados"] = results, ["fontes"] = sources, ["modelo"] = Model };
            }
            // Echo the complete content, including thought signatures and IDs.
            contents.Add(content.DeepClone());
            var responses = new JsonArray();
            foreach (var part in calls.Take(6))
            {
                var call = part["functionCall"]!;
                var name = call["name"]?.GetValue<string>() ?? "";
                var args = call["args"]?.ToJsonString() ?? "{}";
                JsonNode toolResult;
                try
                {
                    Filters filters;
                    if (name == "buscar_empresas")
                    {
                        var query = WireJson.Read<ToolSearch>(args);
                        filters = query.Filtros;
                        var found = selectedDatabase.Search(new() { Filtros = filters, PorPagina = query.Limite }, cancellation);
                        results.Add(Node(new { tipo = "empresas", titulo = "Empresas encontradas", filtros = filters, total = found.Total, itens = found.Itens }));
                        // Free-text filters stay in the local UI. Small result sets
                        // do not send exact counts back to the provider.
                        var small = found.Total is > 0 and < 5;
                        toolResult = Node(new { total = small ? (long?)null : found.Total,
                            exibidas = small ? (int?)null : found.Itens.Count,
                            nota = "Os contatos e filtros detalhados serão exibidos na aplicação. Contagens de recortes com menos de cinco empresas são omitidas do envio ao Gemini." })!;
                    }
                    else if (name == "analisar_base")
                    {
                        var query = WireJson.Read<AnalysisRequest>(args);
                        filters = query.Filtros;
                        var rows = selectedDatabase.Analyze(query, cancellation);
                        results.Add(Node(new { tipo = "analise", titulo = "Análise da base", itens = rows, filtros = filters, agrupar_por = query.AgruparPor }));
                        toolResult = Node(new { itens = rows.Where(row => Convert.ToInt64(row["contatos"]) >= 5), limitado_a = query.Limite,
                            nota = "Grupos com menos de cinco empresas são omitidos do envio ao Gemini. A análise completa e os filtros aparecem na aplicação." })!;
                    }
                    else throw new ApiError("Ferramenta não permitida.", 422);
                    var source = Node(new { fonte = $"Receita Federal — recorte {selectedDatabase.Uf}", versao = metadata?["versao_receita"] ?? "", filtros = filters })!;
                    if (!sources.Any(s => JsonNode.DeepEquals(s, source))) sources.Add(source);
                }
                catch (ApiError error) { toolResult = new JsonObject { ["erro"] = error.Status == 422 ? "Argumentos inválidos. Corrija conforme o esquema das ferramentas." : error.Message }; }
                catch (Microsoft.Data.Sqlite.SqliteException) { toolResult = new JsonObject { ["erro"] = Database.QueryFailure }; }
                var response = new JsonObject { ["name"] = name, ["response"] = toolResult };
                if (call["id"] is not null) response["id"] = call["id"]!.DeepClone();
                responses.Add(new JsonObject { ["functionResponse"] = response });
            }
            contents.Add(new JsonObject { ["role"] = "user", ["parts"] = responses });
        }
        throw new ApiError("A pergunta precisou de consultas demais. Tente dividi-la em perguntas menores.");
    }

    private static JsonObject Message(string role, string text) => new()
    { ["role"] = role, ["parts"] = new JsonArray(new JsonObject { ["text"] = text }) };
}
