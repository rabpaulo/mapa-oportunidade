using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Ceara.Api;

public sealed partial class Database(IConfiguration config)
{
    public string DataDirectory => Path.GetFullPath(config["CEARA_DATA_DIR"] ?? "data");
    public string Uf { get; private init; } = "CE";
    public Database ForUf(string? uf) => new(config) { Uf = Geography.Resolve(uf, config) };
    public string PathName => Path.Combine(DataDirectory, "uf", Uf, "contatos.db");
    public const string PublicColumns = "id,cnpj,cidade,cod_municipio,segmento,oportunidade,porte,abertura,score";
    private string SelectedColumns => Restricted ? string.Join(',', PublicColumns.Split(',').Select(v => "c." + v)) : "c.*";
    private string Metrics => Restricted ? "COUNT(*) AS contatos, COALESCE(ROUND(AVG(c.score), 2), 0) AS score_medio" : Aggregates;
    private string CachedTotals => "COALESCE(SUM(contatos),0) AS contatos," + (Restricted ? "" : "COALESCE(SUM(com_email),0) AS com_email,COALESCE(SUM(com_celular),0) AS com_celular,COALESCE(SUM(sem_dominio),0) AS sem_dominio,")
        + "COALESCE(ROUND(SUM(score_medio*contatos)/NULLIF(SUM(contatos),0),2),0) AS score_medio";
    public bool Restricted => PublicationProfile.Get(config) == PublicationProfile.Minimized;
    public const string QueryFailure = "Não foi possível concluir a consulta à base. Tente um recorte menor ou verifique os dados.";
    public static readonly IReadOnlyDictionary<string, string> Orders = new Dictionary<string, string>
    {
        ["cnpj"] = "c.cnpj, c.id", ["score"] = "c.score DESC, c.cidade, c.nome, c.id", ["nome"] = "c.nome, c.id",
        ["cidade"] = "c.cidade, c.score DESC, c.id", ["recente"] = "c.abertura DESC, c.score DESC, c.id",
        ["antiga"] = "c.abertura, c.score DESC, c.id"
    };
    private const string Aggregates = """
        COUNT(*) AS contatos,
        SUM(CASE WHEN c.email <> '' THEN 1 ELSE 0 END) AS com_email,
        SUM(c.tem_celular) AS com_celular,
        SUM(CASE WHEN c.email <> '' AND c.dominio_proprio=0 THEN 1 ELSE 0 END) AS sem_dominio,
        COALESCE(ROUND(AVG(c.score), 2), 0) AS score_medio
        """;

    [GeneratedRegex("[a-z0-9]+", RegexOptions.CultureInvariant)] private static partial Regex Tokens();
    public static string Normalized(string? text) => string.Concat((text ?? "").Normalize(NormalizationForm.FormKD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToLowerInvariant().Trim();
    public static string FtsExpression(string text) => string.Join(' ', Tokens().Matches(Normalized(text)).Select(m => $"\"{m.Value}\"*"));

    // A query owns its connection and cancellation hook. Disabling pooling also
    // allows the local pipeline to publish a new file without stale pooled handles.
    private sealed class Session : IDisposable
    {
        public SqliteConnection Connection { get; }
        private readonly CancellationTokenSource deadline;
        private readonly CancellationTokenRegistration interrupt;
        public Session(string path, CancellationToken cancellation, int timeoutSeconds = 15)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!File.Exists(path)) throw new DatabaseUnavailable("A base desta UF ainda não foi preparada. Consulte a aba Base.");
            Connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 10
            }.ToString());
            Connection.Open();
            Connection.CreateFunction<string?, string>("normalizar", Normalized, isDeterministic: true);
            deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            interrupt = deadline.Token.Register(() => SQLitePCL.raw.sqlite3_interrupt(Connection.Handle));
        }
        public void Dispose() { interrupt.Dispose(); deadline.Dispose(); Connection.Dispose(); }
    }

    private Session Open(CancellationToken cancellation, int timeoutSeconds = 15)
    {
        var session = new Session(PathName, cancellation, timeoutSeconds);
        try
        {
            Execute(session.Connection, "PRAGMA query_only=ON");
            if (Scalar(session.Connection, "SELECT valor FROM meta WHERE chave='uf'")?.ToString() != Uf)
                throw new DatabaseUnavailable($"O banco configurado não é uma base de {Uf}.");
            if (Restricted && Scalar(session.Connection, "SELECT valor FROM meta WHERE chave='publicacao_restrita'")?.ToString() != "1")
                throw new DatabaseUnavailable("A publicação exige uma base minimizada. O cadastro original não pode ser hospedado neste modo.");
            if (Restricted && Scalar(session.Connection, "SELECT valor FROM meta WHERE chave='politica_publicacao'")?.ToString() != PublicationProfile.Policy)
                throw new DatabaseUnavailable("A base pública exige a política minimizada v2.");
            return session;
        }
        catch { session.Dispose(); throw; }
    }

    private static SqliteCommand Command(SqliteConnection connection, string sql, IEnumerable<object?>? parameters = null)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        if (parameters is not null)
            foreach (var (value, index) in parameters.Select((value, index) => (value, index)))
                command.Parameters.AddWithValue($"@p{index}", value ?? DBNull.Value);
        return command;
    }
    private static object? Scalar(SqliteConnection connection, string sql, IEnumerable<object?>? parameters = null)
    {
        using var command = Command(connection, sql, parameters);
        return command.ExecuteScalar();
    }
    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = Command(connection, sql); command.ExecuteNonQuery();
    }
    private static List<Dictionary<string, object?>> Rows(SqliteConnection connection, string sql, IEnumerable<object?>? parameters = null)
    {
        using var command = Command(connection, sql, parameters);
        using var reader = command.ExecuteReader();
        var rows = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    private static string Canonical(SqliteConnection connection, string column, string value)
    {
        var sql = column switch
        {
            "cidade" => "SELECT nome FROM municipios", "segmento" => "SELECT nome FROM segmentos",
            "porte" => "SELECT DISTINCT porte AS nome FROM contatos", _ => throw new ArgumentException("Categoria inválida.")
        };
        return Rows(connection, sql).Select(r => r["nome"]?.ToString() ?? "")
            .FirstOrDefault(v => Normalized(v) == Normalized(value)) ?? value.Trim();
    }

    private (string Source, string Condition, List<object?> Params) Where(SqliteConnection connection, Filters filters)
    {
        ValidateFilters(filters);
        var source = "contatos c";
        var clauses = new List<string>();
        var parameters = new List<object?>();
        string Add(object? value) { var name = $"@p{parameters.Count}"; parameters.Add(value); return name; }
        if (!string.IsNullOrWhiteSpace(filters.Termo))
        {
            var expression = FtsExpression(filters.Termo);
            if (expression.Length > 0) { source = "contatos_fts JOIN contatos c ON c.id=contatos_fts.rowid"; clauses.Add($"contatos_fts MATCH {Add(expression)}"); }
            else clauses.Add("0=1");
        }
        foreach (var (column, value) in new[] { ("cidade", filters.Cidade), ("segmento", filters.Segmento), ("porte", filters.Porte), ("bairro", filters.Bairro) })
            if (!string.IsNullOrWhiteSpace(value))
                clauses.Add(column == "bairro" ? $"normalizar(c.bairro) = {Add(Normalized(value))}" : $"c.{column} = {Add(Canonical(connection, column, value))}");
        foreach (var (column, values) in new[] { ("cidade", filters.Cidades), ("segmento", filters.Segmentos) })
            if (values.Length > 0) clauses.Add($"c.{column} IN ({string.Join(',', values.Select(v => Add(Canonical(connection, column, v))))})");
        if (filters.ScoreMinimo > 0) clauses.Add($"c.score >= {Add(filters.ScoreMinimo)}");
        if (filters.AnoMinimo is not null) clauses.Add($"c.abertura >= {Add(filters.AnoMinimo.Value.ToString(CultureInfo.InvariantCulture))}");
        if (filters.AnoMaximo is not null) clauses.Add($"c.abertura <= {Add(filters.AnoMaximo.Value.ToString(CultureInfo.InvariantCulture))}");
        if (filters.SomenteCelular) clauses.Add("c.tem_celular = 1");
        if (filters.SomenteEmail) clauses.Add("c.email <> ''");
        if (filters.SomenteSemDominio) clauses.Add("c.email <> '' AND c.dominio_proprio = 0");
        return (source, clauses.Count == 0 ? "1=1" : string.Join(" AND ", clauses), parameters);
    }

    private Dictionary<string, object?> Contact(Dictionary<string, object?> row)
    {
        if (Restricted)
        {
            var permitted = PublicColumns.Split(',');
            return row.Where(pair => permitted.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
        }
        foreach (var name in new[] { "dominio_proprio", "tem_celular" }) row[name] = Convert.ToInt64(row[name] ?? 0) != 0;
        return row;
    }
    public ContactPage Search(SearchRequest request, CancellationToken cancellation = default)
    {
        request.Validate();
        using var session = Open(cancellation);
        var (source, condition, parameters) = Where(session.Connection, request.Filtros);
        var total = Convert.ToInt64(Scalar(session.Connection, $"SELECT COUNT(*) FROM {source} WHERE {condition}", parameters));
        var count = parameters.Count;
        var rows = Rows(session.Connection, $"SELECT {SelectedColumns} FROM {source} WHERE {condition} ORDER BY {Order(request.Filtros)} LIMIT @p{count} OFFSET @p{count + 1}",
            [.. parameters, request.PorPagina, (long)(request.Pagina - 1) * request.PorPagina]);
        return new(total, rows.Select(Contact).ToList());
    }
    public Dictionary<string, object?>? Detail(long id, CancellationToken cancellation = default)
    {
        using var session = Open(cancellation);
        var row = Rows(session.Connection, $"SELECT {SelectedColumns} FROM contatos c WHERE id=@p0", [id]).FirstOrDefault();
        return row is null ? null : Contact(row);
    }
    public Dictionary<string, object?> Metadata(CancellationToken cancellation = default)
    {
        using var session = Open(cancellation);
        var meta = Rows(session.Connection, "SELECT chave, valor FROM meta").ToDictionary(r => r["chave"]!.ToString()!, r => r["valor"]);
        foreach (var pair in Rows(session.Connection, $"SELECT {CachedTotals} FROM municipios")[0]) meta[pair.Key] = pair.Value;
        meta["municipios"] = Scalar(session.Connection, "SELECT COUNT(*) FROM municipios");
        meta["municipios_mapeados"] = Scalar(session.Connection, "SELECT COUNT(*) FROM municipios WHERE codigo_ibge IS NOT NULL AND codigo_ibge<>''");
        meta["contatos_sem_malha"] = Scalar(session.Connection, "SELECT COALESCE(SUM(contatos),0) FROM municipios WHERE codigo_ibge IS NULL OR codigo_ibge=''");
        meta["ramos"] = Scalar(session.Connection, "SELECT COUNT(*) FROM segmentos");
        meta["codigo_uf"] = Geography.Get(Uf).Codigo;
        meta["nome_uf"] = Geography.Get(Uf).Nome;
        meta["total_municipios"] = Geography.Get(Uf).TotalMunicipios;
        meta["tamanho_mb"] = Math.Round(new FileInfo(PathName).Length / 1e6, 1);
        return meta;
    }
    public List<Dictionary<string, object?>> Areas(string kind, CancellationToken cancellation = default)
    {
        if (kind is not ("municipios" or "segmentos")) throw new ArgumentException("Agrupamento inválido.");
        using var session = Open(cancellation);
        var code = kind == "municipios" ? "codigo_ibge" : "nome";
        var mapped = kind == "municipios" ? " WHERE codigo_ibge IS NOT NULL AND codigo_ibge<>''" : "";
        return Rows(session.Connection, $"SELECT {code} AS codigo, nome, contatos, {(Restricted ? "" : "com_email, com_celular, sem_dominio,")} score_medio FROM {kind}{mapped} ORDER BY contatos DESC, nome");
    }
    public List<Dictionary<string, object?>> Analyze(AnalysisRequest request, CancellationToken cancellation = default)
    {
        request.Validate();
        if (Restricted && (request.AgruparPor.Contains("bairro") || request.OrdenarPor is not ("contatos" or "score_medio")))
            throw new ApiError("A versão pública não permite recortes por bairro.", 422);
        using var session = Open(cancellation);
        var (source, condition, parameters) = Where(session.Connection, request.Filtros);
        source = AggregateSource(source, request.Filtros);
        var columns = string.Join(", ", request.AgruparPor.Distinct().Select(g => "c." + g));
        var prefix = columns.Length == 0 ? "" : columns + ", ";
        var group = columns.Length == 0 ? "" : " GROUP BY " + columns;
        var groups = request.AgruparPor.Distinct().ToArray();
        var rows = condition == "1=1" && groups.Length == 0 ? Rows(session.Connection, $"SELECT {CachedTotals} FROM municipios")
            : condition == "1=1" && groups.Length == 1 && groups[0] is ("cidade" or "segmento") ? CachedAreas(session.Connection, groups[0], request.OrdenarPor, request.Limite)
            : Rows(session.Connection, $"SELECT {prefix}{Metrics} FROM {source} WHERE {condition}{group} ORDER BY {request.OrdenarPor} DESC LIMIT @p{parameters.Count}", [.. parameters, request.Limite]);
        AddPercentages(rows);
        return rows;
    }

    private void AddPercentages(List<Dictionary<string, object?>> rows)
    {
        foreach (var row in rows)
            foreach (var metric in Restricted ? Array.Empty<string>() : new[] { "com_email", "com_celular", "sem_dominio" })
            {
                row[metric] ??= 0L;
                var total = Convert.ToDouble(row["contatos"]);
                row["percentual_" + metric] = total > 0 ? Math.Round(Convert.ToDouble(row[metric]) * 100 / total, 2) : 0;
            }
    }
    public Dictionary<string, object?> Facets(Filters filters, CancellationToken cancellation = default)
    {
        ValidateFilters(filters);
        var result = new Dictionary<string, object?>();
        foreach (var (key, group, cleared) in new[]
        {
            ("municipios", "cidade", filters with { Cidade = "", Cidades = [] }),
            ("segmentos", "segmento", filters with { Segmento = "", Segmentos = [] })
        })
        {
            using var session = Open(cancellation);
            var (source, condition, parameters) = Where(session.Connection, cleared);
            source = AggregateSource(source, cleared);
            var rows = condition == "1=1" ? CachedAreas(session.Connection, group, "contatos")
                : Rows(session.Connection, $"SELECT c.{group},{Metrics} FROM {source} WHERE {condition} GROUP BY c.{group} ORDER BY contatos DESC,c.{group}", parameters);
            AddPercentages(rows);
            foreach (var row in rows) { row["codigo"] = row[group]; row["nome"] = row[group]; row.Remove(group); }
            result[key] = rows;
        }
        return result;
    }

    private List<Dictionary<string, object?>> CachedAreas(SqliteConnection connection, string group, string order, int? limit = null)
    {
        var table = group == "cidade" ? "municipios" : "segmentos";
        return Rows(connection, $"SELECT nome AS {group},{CachedTotals} FROM {table} GROUP BY nome ORDER BY {order} DESC,nome" + (limit is null ? "" : " LIMIT @p0"), limit is null ? null : [limit.Value]);
    }

    private static string AggregateSource(string source, Filters filters)
    {
        // A grouping index otherwise causes millions of scattered table reads
        // when the only predicates are contact flags, years or a neighborhood.
        var indexedFilter = filters.Cidade.Length > 0 || filters.Cidades.Length > 0 || filters.Segmento.Length > 0
            || filters.Segmentos.Length > 0 || filters.Porte.Length > 0 || filters.ScoreMinimo > 0;
        return source == "contatos c" && !indexedFilter ? "contatos c NOT INDEXED" : source;
    }

    public void ValidateSnapshot()
    {
        using var session = Open(default);
        var connection = session.Connection;
        if (Scalar(connection, "PRAGMA quick_check")?.ToString() != "ok") throw new DatabaseUnavailable("A nova base falhou na verificação de integridade.");
        if (Convert.ToInt64(Scalar(connection, "SELECT COUNT(*) FROM contatos")) == 0) throw new DatabaseUnavailable("A nova base não contém contatos.");
        if (Convert.ToInt64(Scalar(connection, "SELECT COUNT(*) FROM municipios WHERE uf IS NULL OR uf <> @p0 OR (codigo_ibge<>'' AND codigo_ibge NOT LIKE @p1)", [Uf, Geography.Get(Uf).Codigo + "%"])) > 0)
            throw new DatabaseUnavailable("A nova base possui municípios incompatíveis com a UF.");
        var missing = Rows(connection, "SELECT cod_municipio,nome,contatos FROM municipios WHERE codigo_ibge IS NULL OR codigo_ibge=''");
        if (missing.Count > 0 && (Restricted || Uf != "SC" || missing.Any(row => row["cod_municipio"]?.ToString() != "0403" || Normalized(row["nome"]?.ToString()) != "acara")
            || Scalar(connection, "SELECT valor FROM meta WHERE chave='municipios_sem_malha'")?.ToString() != missing.Count.ToString(CultureInfo.InvariantCulture)
            || Scalar(connection, "SELECT valor FROM meta WHERE chave='contatos_sem_malha'")?.ToString() != missing.Sum(row => Convert.ToInt64(row["contatos"])).ToString(CultureInfo.InvariantCulture)))
            throw new DatabaseUnavailable("A nova base possui municípios sem correspondência verificada com a UF.");
        Rows(connection, "SELECT rowid FROM contatos_fts WHERE contatos_fts MATCH 'fortaleza*' LIMIT 1");
        if (Restricted) ValidatePublicSchema(connection);
    }

    public void ValidateFilters(Filters filters)
    {
        filters.Validate();
        if (Restricted && (filters.Bairro.Length > 0 || filters.SomenteCelular || filters.SomenteEmail || filters.SomenteSemDominio || filters.Ordem == "nome"))
            throw new ApiError("A publicação não permite filtros de nomes, contatos ou endereço.", 422);
    }
    private string Order(Filters filters) => Restricted && filters.Ordem == "score" ? "c.score DESC,c.cidade,c.cnpj,c.id" : Orders[filters.Ordem];

    internal static void ValidatePublicSchema(SqliteConnection connection)
    {
        var meta = Rows(connection, "SELECT chave,valor FROM meta").ToDictionary(row => row["chave"]!.ToString()!, row => row["valor"]?.ToString());
        if (meta.GetValueOrDefault("uf") != "CE" || meta.GetValueOrDefault("publicacao_restrita") != "1"
            || meta.GetValueOrDefault("politica_publicacao") != PublicationProfile.Policy || string.IsNullOrEmpty(meta.GetValueOrDefault("versao_receita")))
            throw new DatabaseUnavailable("O banco público exige a política minimizada v2 e versão identificada do Ceará.");
        if (Convert.ToInt64(Scalar(connection, "SELECT COUNT(*) FROM municipios WHERE uf IS NULL OR uf<>'CE' OR codigo_ibge IS NULL OR codigo_ibge NOT LIKE '23%'")) > 0)
            throw new DatabaseUnavailable("O banco público contém municípios de outra UF ou não verificados.");
        if (Convert.ToInt64(Scalar(connection, "SELECT COUNT(*) FROM contatos WHERE typeof(cnpj)<>'text' OR length(cnpj)<>14 OR cnpj GLOB '*[^A-Z0-9]*' OR substr(cnpj,13) GLOB '*[^0-9]*'")) > 0)
            throw new DatabaseUnavailable("A publicação exige CNPJs textuais no formato numérico ou alfanumérico.");
        var expected = new Dictionary<string,string[]>
        {
            ["contatos"] = PublicColumns.Split(','), ["meta"] = ["chave","valor"],
            ["municipios"] = ["cod_municipio","codigo_ibge","nome","uf","contatos","score_medio"],
            ["segmentos"] = ["nome","contatos","score_medio"],
            ["contatos_fts"] = ["cnpj","cidade","segmento","porte"]
        };
        var allowed = expected.Keys.Concat(new[] { "contatos_fts", "contatos_fts_data", "contatos_fts_idx", "contatos_fts_docsize", "contatos_fts_config" }).ToHashSet();
        var tables = Rows(connection, "SELECT name FROM sqlite_master WHERE type='table'").Select(r => r["name"]!.ToString()!).ToHashSet();
        if (!tables.SetEquals(allowed)) throw new DatabaseUnavailable("A base pública contém tabelas não autorizadas.");
        foreach (var (table, fields) in expected)
            if (!Rows(connection, $"PRAGMA table_info({table})").Select(r => r["name"]!.ToString()!).ToHashSet().SetEquals(fields))
                throw new DatabaseUnavailable("A base pública contém campos privados ou esquema antigo.");
    }

    public async Task Export(Filters filters, Stream destination, CancellationToken cancellation)
    {
        ValidateFilters(filters);
        using var session = Open(cancellation, 600);
        var (source, condition, parameters) = Where(session.Connection, filters);
        using var command = Command(session.Connection, $"SELECT {SelectedColumns} FROM {source} WHERE {condition} ORDER BY {Order(filters)}", parameters);
        using var reader = command.ExecuteReader();
        await using var gzip = new System.IO.Compression.GZipStream(destination, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true);
        await using var writer = new StreamWriter(gzip, new UTF8Encoding(false), bufferSize: 65536, leaveOpen: true);
        static string Cell(object? value)
        {
            var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            if (text.TrimStart().StartsWith('=') || text.TrimStart().StartsWith('+') || text.TrimStart().StartsWith('-') || text.TrimStart().StartsWith('@')) text = "'" + text;
            return '\"' + text.Replace("\"", "\"\"") + '\"';
        }
        await writer.WriteLineAsync(string.Join(',', Enumerable.Range(0, reader.FieldCount).Select(i => Cell(reader.GetName(i)))).AsMemory(), cancellation);
        while (reader.Read())
        {
            cancellation.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(',', Enumerable.Range(0, reader.FieldCount).Select(i => Cell(reader.IsDBNull(i) ? null : reader.GetValue(i)))).AsMemory(), cancellation);
        }
    }
}
