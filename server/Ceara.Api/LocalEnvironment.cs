namespace Ceara.Api;

internal static class LocalEnvironment
{
    public static void Load(ConfigurationManager config)
    {
        // Only local launches read .env. Images never contain this file.
        if (config["VERCEL"] == "1" || config["CEARA_CONTAINER"] == "1") return;
        var root = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (root.Parent is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) root = root.Parent;
        var path = Path.Combine(root.FullName, ".env");
        var values = new Dictionary<string, string?>();
        foreach (var line in File.Exists(path) ? File.ReadLines(path) : [])
        {
            var text = line.Trim();
            if (text.StartsWith('#') || !text.Contains('=')) continue;
            var parts = text.Split('=', 2);
            var name = parts[0].Trim();
            if (config[name] is null) values[name] = parts[1].Trim().Trim('"', '\'');
        }
        config.AddInMemoryCollection(values);
        config["CEARA_DATA_DIR"] ??= Path.Combine(root.FullName, "data");
    }
}
