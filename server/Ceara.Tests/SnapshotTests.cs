using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Ceara.Api;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ceara.Tests;

public sealed class SnapshotTests
{
    [Theory] [InlineData("valid")] [InlineData("brotli")] [InlineData("full-public")] [InlineData("archive-hash")] [InlineData("file-hash")] [InlineData("traversal")] [InlineData("foreign-uf")] [InlineData("version")] [InlineData("invalid-map")]
    public async Task RestoreValidatesBeforePublishing(string mode)
    {
        using var host = new TestHost();
        if (mode == "foreign-uf") host.Sql("UPDATE meta SET valor='SC' WHERE chave='uf'");
        var files = new Dictionary<string, byte[]>
        {
            [Snapshot.RequiredFiles[0]] = await File.ReadAllBytesAsync(Path.Combine(host.DirectoryPath, "uf", "CE", "contatos.db")),
            [Snapshot.RequiredFiles[1]] = System.Text.Encoding.UTF8.GetBytes(mode == "invalid-map" ? "{\"type\":\"Polygon\"}" : "{\"type\":\"FeatureCollection\",\"features\":[]}"),
            [Snapshot.RequiredFiles[2]] = "{\"type\":\"FeatureCollection\",\"features\":[]}"u8.ToArray()
        };
        var archive = Path.Combine(host.DirectoryPath, mode == "brotli" ? "snapshot.tar.br" : "snapshot.tar.gz");
        using (var output = File.Create(archive))
        using (Stream compressed = mode == "brotli" ? new BrotliStream(output, CompressionMode.Compress) : new GZipStream(output, CompressionMode.Compress))
        using (var tar = new TarWriter(compressed, TarEntryFormat.Ustar))
        {
            foreach (var (name, content) in files)
            {
                using var stream = new MemoryStream(content);
                var entry = new UstarTarEntry(TarEntryType.RegularFile, mode == "traversal" && name == Snapshot.RequiredFiles[0] ? "../outside.db" : name) { DataStream = stream };
                tar.WriteEntry(entry);
            }
        }
        static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
        var manifest = new Snapshot.Manifest(null, mode == "version" ? "wrong" : "2026-09-14/2026-10-04 17:12", mode == "archive-hash" ? new string('0', 64) : Hash(await File.ReadAllBytesAsync(archive)), files.ToDictionary(pair => pair.Key, pair => mode == "file-hash" ? new string('0', 64) : Hash(pair.Value)));
        var manifestPath = Path.Combine(host.DirectoryPath, "snapshot.json"); await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, WireJson.Options));
        var destination = Path.Combine(host.DirectoryPath, "restored");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CEARA_DATA_DIR"] = destination, ["CEARA_SNAPSHOT_ARCHIVE"] = archive, ["CEARA_SNAPSHOT_MANIFEST"] = manifestPath,
            ["CEARA_PUBLIC"] = mode == "full-public" ? "1" : null,
            ["CEARA_DATA_PROFILE"] = mode == "full-public" ? "integral" : null
        }).Build();
        var db = new Database(config);
        if (mode is "valid" or "brotli") { await Snapshot.Restore(config, db); Assert.Equal(5, db.Search(new()).Total); await Snapshot.Restore(config, db); Assert.Equal(5, db.Search(new()).Total); }
        else { await Assert.ThrowsAnyAsync<Exception>(() => Snapshot.Restore(config, db)); Assert.False(Directory.Exists(destination)); }
        Assert.Empty(Directory.GetDirectories(host.DirectoryPath, "*.preparando"));
        Assert.False(File.Exists(Path.Combine(host.DirectoryPath, "outside.db")));
    }
}
