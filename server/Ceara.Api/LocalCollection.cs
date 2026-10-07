using System.Diagnostics;

namespace Ceara.Api;

public sealed record CollectionRequest : ValidatedRequest
{
    public string[] Ufs { get; set; } = [];
    public bool Reaproveitar { get; set; }
    public override void Validate()
    {
        if (Ufs is null || Ufs.Length is < 1 or > 27 || Ufs.Any(uf => uf is null || !Geography.All.Any(state => state.Uf == uf)))
            throw new ApiError("Escolha pelo menos uma UF válida para baixar.", 422);
    }
}

public sealed record CollectionStatus(string Status, string[] Ufs, string[] Linhas);

public interface ILocalCollector
{
    Task<int> Run(CollectionRequest request, Action<string> log, CancellationToken cancellation);
}

// Uses the same transactional pipeline and cross-process lock as the CLI.
public sealed class PythonCollector(IConfiguration config, Database database) : ILocalCollector
{
    public async Task<int> Run(CollectionRequest request, Action<string> log, CancellationToken cancellation)
    {
        var root = config["CEARA_PROJECT_ROOT"] ?? LocalEnvironment.ProjectRoot();
        var script = Path.Combine(root, "scripts", "coletar.py");
        var python = config["CEARA_PYTHON"] ?? Path.Combine(root, ".venv", OperatingSystem.IsWindows() ? "Scripts/python.exe" : "bin/python");
        if (!File.Exists(script) || !File.Exists(python))
            throw new ApiError("Prepare o Python com ./scripts/preparar_python.sh antes de baixar estados.");
        var start = new ProcessStartInfo(python)
        {
            WorkingDirectory = root, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var arg in new[] { "-u", script, "--dados", database.DataDirectory, "--uf" }.Concat(request.Ufs)) start.ArgumentList.Add(arg);
        if (request.Reaproveitar) start.ArgumentList.Add("--reaproveitar");
        // The collector has no need for Gemini credentials.
        start.Environment.Remove("GEMINI_API_KEY");
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        using var process = Process.Start(start) ?? throw new ApiError("Não foi possível iniciar a coleta.");
        using var registration = cancellation.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
        async Task Pump(StreamReader reader)
        {
            while (await reader.ReadLineAsync(cancellation) is { } line) log(line);
        }
        await Task.WhenAll(Pump(process.StandardOutput), Pump(process.StandardError), process.WaitForExitAsync(cancellation));
        return process.ExitCode;
    }
}

public sealed class LocalCollection(IConfiguration config, ILocalCollector collector, IHostApplicationLifetime lifetime)
{
    private readonly object gate = new();
    private string status = "ocioso";
    private string[] ufs = [];
    private readonly Queue<string> lines = new();

    private void EnsureLocal()
    {
        if (PublicationProfile.IsPublic(config)) throw new ApiError("A coleta de estados está disponível apenas na execução local.", 403);
    }
    public CollectionStatus Snapshot()
    {
        EnsureLocal();
        lock (gate) return new(status, [.. ufs], [.. lines]);
    }
    public CollectionStatus Start(CollectionRequest request)
    {
        EnsureLocal(); request.Validate();
        lock (gate)
        {
            if (status == "rodando") throw new ApiError("Já existe uma coleta em andamento.", 409);
            ufs = request.Ufs.Distinct().ToArray();
            status = "rodando"; lines.Clear();
            var copy = new CollectionRequest { Ufs = [.. ufs], Reaproveitar = request.Reaproveitar };
            _ = Task.Run(() => Collect(copy));
            return new(status, [.. ufs], []);
        }
    }
    private void Log(string line)
    {
        lock (gate) { lines.Enqueue(line.Length > 2000 ? line[..2000] : line); while (lines.Count > 500) lines.Dequeue(); }
    }
    private async Task Collect(CollectionRequest request)
    {
        try
        {
            var code = await collector.Run(request, Log, lifetime.ApplicationStopping);
            lock (gate) status = code == 0 ? "concluido" : "erro";
            if (code != 0) Log("A coleta falhou. Veja o log; a base anterior foi preservada.");
        }
        catch (OperationCanceledException) { lock (gate) status = "erro"; Log("Coleta interrompida ao encerrar o servidor."); }
        catch (Exception error)
        {
            Log(error is ApiError ? error.Message : "Não foi possível executar a coleta. Verifique o Python, o disco e as permissões da pasta de dados.");
            lock (gate) status = "erro";
        }
    }
}
