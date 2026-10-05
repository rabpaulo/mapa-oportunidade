using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Ceara.Api;

public static class Snapshot
{
    public static readonly string[] RequiredFiles = ["uf/CE/contatos.db", "ibge/malha_23.geojson", "ibge/malha_br.geojson"];
    public sealed record Manifest(string? Url, string Version, string ArchiveSha256, Dictionary<string, string> Files);

    public static async Task Restore(IConfiguration configuration, Database database)
    {
        var archive = configuration["CEARA_SNAPSHOT_ARCHIVE"];
        if (string.IsNullOrEmpty(archive)) return;
        var manifestPath = configuration["CEARA_SNAPSHOT_MANIFEST"] ?? Path.Combine(Path.GetDirectoryName(archive)!, "snapshot.json");
        var manifest = JsonSerializer.Deserialize<Manifest>(await File.ReadAllTextAsync(manifestPath), WireJson.Options)
            ?? throw new InvalidDataException("Manifesto da base ausente.");
        if (manifest.Files.Count != RequiredFiles.Length || RequiredFiles.Any(name => !manifest.Files.ContainsKey(name)))
            throw new InvalidDataException("O snapshot deve conter o banco CE e as duas malhas.");
        using (var stream = File.OpenRead(archive))
            if (!EqualsHash(Convert.ToHexString(await SHA256.HashDataAsync(stream)), manifest.ArchiveSha256))
                throw new InvalidDataException("O checksum do snapshot é inválido.");
        var destination = database.DataDirectory;
        var marker = Path.Combine(destination, ".snapshot-validated");
        if (Directory.Exists(destination))
        {
            if (!File.Exists(marker) || !EqualsHash((await File.ReadAllTextAsync(marker)).Trim(), manifest.ArchiveSha256))
                throw new InvalidDataException("O destino contém outra versão da base. Use uma pasta vazia.");
            return;
        }
        var staging = destination + "." + Guid.NewGuid().ToString("N") + ".preparando";
        Directory.CreateDirectory(staging);
        try
        {
            using var input = File.OpenRead(archive);
            using Stream decompressed = archive.EndsWith(".br", StringComparison.OrdinalIgnoreCase)
                ? new BrotliStream(input, CompressionMode.Decompress) : new GZipStream(input, CompressionMode.Decompress);
            using var tar = new TarReader(decompressed);
            var seen = new HashSet<string>();
            while (await tar.GetNextEntryAsync() is { } entry)
            {
                // Extract only exact manifest names; reject links, extra files,
                // duplicates and traversal rather than trusting tar paths.
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)
                    || !manifest.Files.ContainsKey(entry.Name) || !seen.Add(entry.Name) || entry.DataStream is null)
                    throw new InvalidDataException("Arquivo inesperado no snapshot.");
                var path = Path.Combine(staging, entry.Name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using (var output = File.Create(path)) await entry.DataStream.CopyToAsync(output);
                using (var stream = File.OpenRead(path))
                    if (!EqualsHash(Convert.ToHexString(await SHA256.HashDataAsync(stream)), manifest.Files[entry.Name]))
                        throw new InvalidDataException("O checksum de um arquivo da base é inválido.");
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.GroupRead);
            }
            if (!seen.SetEquals(RequiredFiles)) throw new InvalidDataException("O snapshot está incompleto.");
            var stagingConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CEARA_DATA_DIR"] = staging }).Build();
            var stagedDatabase = new Database(stagingConfig);
            stagedDatabase.ValidateSnapshot();
            var meta = stagedDatabase.Metadata();
            if (manifest.Version != $"{meta["versao_receita"]}/{meta["gerado_em"]}")
                throw new InvalidDataException("A versão da base não corresponde ao manifesto.");
            foreach (var name in RequiredFiles.Skip(1))
            {
                using var geo = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(staging, name)));
                if (!geo.RootElement.TryGetProperty("type", out var type) || type.GetString() != "FeatureCollection"
                    || !geo.RootElement.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Malha GeoJSON inválida no snapshot.");
            }
            await File.WriteAllTextAsync(Path.Combine(staging, ".snapshot-validated"), manifest.ArchiveSha256);
            Directory.Move(staging, destination);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    private static bool EqualsHash(string actual, string expected) => actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
}
