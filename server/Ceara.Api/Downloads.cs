using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Ceara.Api;

public sealed class Downloads(Database database)
{
    public sealed record Item(string Id, string Arquivo, string Uf, string Versao, string Geracao, string Formato, long Registros, long TamanhoBytes, string Sha256);
    private static string Key(Dictionary<string, object?> meta) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{meta["versao_receita"]}/{meta["gerado_em"]}/{meta.GetValueOrDefault("geracao", "")}"))).ToLowerInvariant()[..24];
    private static Dictionary<string,string> Files(string uf) => new()
    {
        ["contatos"] = "contatos.csv.gz", ["municipios"] = "municipios.csv.gz", ["segmentos"] = "segmentos.csv.gz",
        ["banco"] = "contatos.db", ["metadados"] = "metadados.json", ["malha"] = $"malha_{Geography.Get(uf).Codigo}.geojson", ["brasil"] = "malha_br.geojson"
    };
    private (string Folder, Item[] Items) Read(Database db, CancellationToken cancellation)
    {
        var meta = db.Metadata(cancellation);
        var key = Key(meta);
        var folder = Path.Combine(db.DataDirectory, "uf", db.Uf, "downloads", key);
        var path = Path.Combine(folder, "catalogo.json");
        if (!File.Exists(path)) return (folder, []);
        if (new FileInfo(path).Length > 100_000) throw new DatabaseUnavailable("Catálogo de downloads inválido.");
        var items = JsonSerializer.Deserialize<Item[]>(File.ReadAllText(path), WireJson.Options) ?? [];
        var allowed = Files(db.Uf);
        if (items.Length > allowed.Count || items.Select(i => i.Id).Distinct().Count() != items.Length
            || items.Any(i => !allowed.TryGetValue(i.Id, out var file) || file != i.Arquivo || i.Uf != db.Uf || i.Geracao != key
                || i.Versao != $"{meta["versao_receita"]}/{meta["gerado_em"]}" || i.TamanhoBytes < 0 || i.Registros < 0
                || i.Sha256.Length != 64 || i.Sha256.Any(c => !char.IsAsciiHexDigit(c))))
            throw new DatabaseUnavailable("Catálogo de downloads inválido.");
        return (folder, items);
    }
    public object[] Catalog(string? uf, CancellationToken cancellation)
    {
        var db = database.ForUf(uf);
        return Read(db, cancellation).Items.Select(i => (object)new
        {
            i.Id, i.Uf, i.Versao, i.Geracao, i.Formato, i.Registros, i.TamanhoBytes, i.Sha256,
            url = $"/api/downloads/{i.Id}?uf={db.Uf}&geracao={i.Geracao}"
        }).ToArray();
    }
    public IResult FileResult(string id, string? uf, string? geracao, CancellationToken cancellation)
    {
        var db = database.ForUf(uf);
        if (!Files(db.Uf).ContainsKey(id)) throw new ApiError("Download não encontrado.", 404);
        var (folder, items) = Read(db, cancellation);
        var item = items.SingleOrDefault(i => i.Id == id);
        if (item is null) throw new ApiError("Download ainda não preparado. Consulte a aba Base.", 404);
        if (geracao is not null && geracao != item.Geracao) throw new ApiError("A base foi atualizada. Recarregue o catálogo de downloads.", 409);
        var path = Path.Combine(folder, item.Arquivo);
        if (!File.Exists(path) || new FileInfo(path).Length != item.TamanhoBytes) throw new DatabaseUnavailable("Arquivo de download ausente ou inválido.");
        var type = item.Formato switch { "csv.gz" => "application/gzip", "sqlite" => "application/vnd.sqlite3", "geojson" => "application/geo+json", _ => "application/json" };
        return Results.File(path, type, $"{db.Uf.ToLowerInvariant()}-{item.Arquivo}", enableRangeProcessing: true);
    }
    public void Validate(CancellationToken cancellation = default)
    {
        var (folder, items) = Read(database, cancellation);
        if (!new[] { "banco", "contatos", "municipios", "segmentos", "metadados", "malha", "brasil" }.ToHashSet().SetEquals(items.Select(i => i.Id)))
            throw new DatabaseUnavailable("Prepare todos os downloads antes de publicar.");
        foreach (var item in items)
        {
            var path = Path.Combine(folder, item.Arquivo);
            using var file = File.OpenRead(path);
            if (file.Length != item.TamanhoBytes || !Convert.ToHexString(SHA256.HashData(file)).Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new DatabaseUnavailable("O checksum de um download é inválido.");
            if (database.Restricted && item.Id == "banco")
            {
                using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=path, Mode=SqliteOpenMode.ReadOnly }.ToString());
                conn.Open(); Database.ValidatePublicSchema(conn);
            }
        }
    }
}
