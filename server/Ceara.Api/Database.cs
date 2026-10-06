using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Ceara.Api;

public sealed partial class Database(IConfiguration config)
{
    public string DataDirectory => Path.GetFullPath(config["CEARA_DATA_DIR"] ?? "data");
    public string PathName => Path.Combine(DataDirectory, "uf", "CE", "contatos.db");
    public bool Restricted => PublicationProfile.Get(config) == PublicationProfile.Minimized;
    public const string QueryFailure = "Não foi possível concluir a consulta à base. Tente um recorte menor ou verifique os dados.";
    public static readonly IReadOnlyDictionary<string, string> Orders = new Dictionary<string, string>
    {
        ["score"] = "c.score DESC, c.cidade, c.nome, c.id", ["nome"] = "c.nome, c.id",
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
        public Session(string path, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!File.Exists(path)) throw new DatabaseUnavailable("A base do Ceará ainda não foi preparada. Consulte a aba Base.");
            Connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 10
            }.ToString());
            Connection.Open();
            Connection.CreateFunction<string?, string>("normalizar", Normalized, isDeterministic: true);
            deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            interrupt = deadline.Token.Register(() => SQLitePCL.raw.sqlite3_interrupt(Connection.Handle));
        }
        public void Dispose() { interrupt.Dispose(); deadline.Dispose(); Connection.Dispose(); }
    }

    private Session Open(CancellationToken cancellation)
    {
        var session = new Session(PathName, cancellation);
        try
        {
            Execute(session.Connection, "PRAGMA query_only=ON");
            if (Scalar(session.Connection, "SELECT valor FROM meta WHERE chave='uf'")?.ToString() != "CE")
                throw new DatabaseUnavailable("O banco configurado não é uma base do Ceará.");
            if (Restricted && Scalar(session.Connection, "SELECT valor FROM meta WHERE chave='publicacao_restrita'")?.ToString() != "1")
                throw new DatabaseUnavailable("A publicação exige uma base minimizada. O cadastro original não pode ser hospedado neste modo.");
            if (config["CEARA_PUBLIC"] == "1" && !Restricted && Scalar(session.Connection, "SELECT valor FROM meta WHERE chave='publicacao_restrita'")?.ToString() == "1")
                throw new DatabaseUnavailable("O perfil integral exige o snapshot da base original, sem minimização.");
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

    private static (string Source, string Condition, List<object?> Params) Where(SqliteConnection connection, Filters filters)
    {
        filters.Validate();
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
            var permitted = new[] { "id", "cnpj", "nome", "empresa", "cidade", "segmento", "oportunidade", "porte", "abertura", "score" };
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
        var rows = Rows(session.Connection, $"SELECT c.* FROM {source} WHERE {condition} ORDER BY {Orders[request.Filtros.Ordem]} LIMIT @p{count} OFFSET @p{count + 1}",
            [.. parameters, request.PorPagina, (long)(request.Pagina - 1) * request.PorPagina]);
        return new(total, rows.Select(Contact).ToList());
    }
    public Dictionary<string, object?>? Detail(long id, CancellationToken cancellation = default)
    {
        using var session = Open(cancellation);
        var row = Rows(session.Connection, "SELECT * FROM contatos WHERE id=@p0", [id]).FirstOrDefault();
        return row is null ? null : Contact(row);
    }
    public Dictionary<string, object?> Metadata(CancellationToken cancellation = default)
    {
        using var session = Open(cancellation);
        var meta = Rows(session.Connection, "SELECT chave, valor FROM meta").ToDictionary(r => r["chave"]!.ToString()!, r => r["valor"]);
        foreach (var pair in Rows(session.Connection, $"SELECT {Aggregates} FROM contatos c")[0]) meta[pair.Key] = pair.Value;
        meta["municipios"] = Scalar(session.Connection, "SELECT COUNT(*) FROM municipios");
        meta["ramos"] = Scalar(session.Connection, "SELECT COUNT(*) FROM segmentos");
        meta["tamanho_mb"] = Math.Round(new FileInfo(PathName).Length / 1e6, 1);
        return meta;
    }
    public List<Dictionary<string, object?>> Areas(string kind, CancellationToken cancellation = default)
    {
        if (kind is not ("municipios" or "segmentos")) throw new ArgumentException("Agrupamento inválido.");
        using var session = Open(cancellation);
        var code = kind == "municipios" ? "codigo_ibge" : "nome";
        return Rows(session.Connection, $"SELECT {code} AS codigo, nome, contatos, com_email, com_celular, sem_dominio, score_medio FROM {kind} ORDER BY contatos DESC, nome");
    }
    public List<Dictionary<string, object?>> Analyze(AnalysisRequest request, CancellationToken cancellation = default)
    {
        request.Validate();
        if (Restricted && (request.Filtros.Bairro.Length > 0 || request.AgruparPor.Contains("bairro")))
            throw new ApiError("A versão pública não permite recortes por bairro.", 422);
        using var session = Open(cancellation);
        var (source, condition, parameters) = Where(session.Connection, request.Filtros);
        var columns = string.Join(", ", request.AgruparPor.Distinct().Select(g => "c." + g));
        var prefix = columns.Length == 0 ? "" : columns + ", ";
        var group = columns.Length == 0 ? "" : " GROUP BY " + columns;
        var rows = Rows(session.Connection, $"SELECT {prefix}{Aggregates} FROM {source} WHERE {condition}{group} ORDER BY {request.OrdenarPor} DESC LIMIT @p{parameters.Count}", [.. parameters, request.Limite]);
        foreach (var row in rows)
            foreach (var metric in new[] { "com_email", "com_celular", "sem_dominio" })
            {
                row[metric] ??= 0L;
                var total = Convert.ToDouble(row["contatos"]);
                row["percentual_" + metric] = total > 0 ? Math.Round(Convert.ToDouble(row[metric]) * 100 / total, 2) : 0;
            }
        return rows;
    }
    public Dictionary<string, object?> Facets(Filters filters, CancellationToken cancellation = default)
    {
        filters.Validate();
        var result = new Dictionary<string, object?>();
        foreach (var (key, group, cleared) in new[]
        {
            ("municipios", "cidade", filters with { Cidade = "", Cidades = [] }),
            ("segmentos", "segmento", filters with { Segmento = "", Segmentos = [] })
        })
        {
            var rows = Analyze(new() { Filtros = cleared, AgruparPor = [group], Limite = 200 }, cancellation);
            foreach (var row in rows) { row["codigo"] = row[group]; row["nome"] = row[group]; row.Remove(group); }
            result[key] = rows;
        }
        return result;
    }

    public void ValidateSnapshot()
    {
        using var session = Open(default);
        var connection = session.Connection;
        if (Scalar(connection, "PRAGMA quick_check")?.ToString() != "ok") throw new DatabaseUnavailable("A nova base falhou na verificação de integridade.");
        if (Convert.ToInt64(Scalar(connection, "SELECT COUNT(*) FROM contatos")) == 0) throw new DatabaseUnavailable("A nova base não contém contatos.");
        if (Convert.ToInt64(Scalar(connection, "SELECT COUNT(*) FROM municipios WHERE uf <> 'CE' OR codigo_ibge IS NULL OR codigo_ibge='' OR codigo_ibge NOT LIKE '23%'")) > 0)
            throw new DatabaseUnavailable("A nova base possui municípios sem correspondência com o Ceará.");
        Rows(connection, "SELECT rowid FROM contatos_fts WHERE contatos_fts MATCH 'fortaleza*' LIMIT 1");
        if (Restricted && Convert.ToInt64(Scalar(connection,
            "SELECT COUNT(*) FROM contatos WHERE COALESCE(email,'')<>'' OR COALESCE(telefone,'')<>'' OR COALESCE(whatsapp,'')<>'' OR COALESCE(endereco,'')<>'' OR COALESCE(bairro,'')<>''")) > 0)
            throw new DatabaseUnavailable("A base pública contém campos privados. Gere uma nova versão minimizada.");
    }
}
