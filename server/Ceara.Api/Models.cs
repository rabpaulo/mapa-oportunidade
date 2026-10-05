using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ceara.Api;

public static class WireJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static T Read<T>(string json) where T : ValidatedRequest
    {
        try
        {
            var request = JsonSerializer.Deserialize<T>(json, Options)
                ?? throw new ApiError("Envie um objeto JSON.", 422);
            request.Validate();
            return request;
        }
        catch (JsonException)
        {
            throw new ApiError("Os dados enviados são inválidos. Confira os campos e tente novamente.", 422);
        }
    }
}

public class ApiError(string message, int status = 503) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed class DatabaseUnavailable(string message) : ApiError(message);

public abstract record ValidatedRequest
{
    public virtual void Validate()
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(this, new ValidationContext(this), errors, true))
            throw new ApiError("Os dados enviados são inválidos. Confira os campos e tente novamente.", 422);
    }

    protected static void Require(bool condition, string message)
    {
        if (!condition) throw new ApiError(message, 422);
    }
}

public sealed record Filters : ValidatedRequest
{
    [Required(AllowEmptyStrings = true), MaxLength(200)] public string Termo { get; init; } = "";
    [Required(AllowEmptyStrings = true), MaxLength(120)] public string Cidade { get; init; } = "";
    [Required, MaxLength(20)] public string[] Cidades { get; init; } = [];
    [Required(AllowEmptyStrings = true), MaxLength(120)] public string Segmento { get; init; } = "";
    [Required, MaxLength(20)] public string[] Segmentos { get; init; } = [];
    [Required(AllowEmptyStrings = true), MaxLength(80)] public string Porte { get; init; } = "";
    [Required(AllowEmptyStrings = true), MaxLength(120)] public string Bairro { get; init; } = "";
    [Range(1800, 2200)] public int? AnoMinimo { get; init; }
    [Range(1800, 2200)] public int? AnoMaximo { get; init; }
    [Range(0, 100)] public int ScoreMinimo { get; init; }
    public bool SomenteCelular { get; init; }
    public bool SomenteEmail { get; init; }
    public bool SomenteSemDominio { get; init; }
    [Required] public string Ordem { get; init; } = "score";

    public override void Validate()
    {
        base.Validate();
        Require(Database.Orders.ContainsKey(Ordem), "Ordenação inválida.");
        Require(AnoMinimo is null || AnoMaximo is null || AnoMinimo <= AnoMaximo,
            "O ano inicial deve ser menor ou igual ao final.");
        Require(Cidades.Concat(Segmentos).All(v => v is not null && v.Length <= 120),
            "Municípios e ramos devem ter até 120 caracteres.");
    }
}

public sealed record SearchRequest : ValidatedRequest
{
    [Required] public Filters Filtros { get; init; } = new();
    [Range(1, 1_000_000)] public int Pagina { get; init; } = 1;
    [Range(1, 200)] public int PorPagina { get; init; } = 50;
    public override void Validate() { base.Validate(); Filtros.Validate(); }
}

public sealed record AnalysisRequest : ValidatedRequest
{
    public static readonly string[] Groups = ["cidade", "bairro", "segmento", "porte", "abertura"];
    public static readonly string[] Metrics = ["contatos", "com_email", "com_celular", "sem_dominio", "score_medio"];
    [Required] public Filters Filtros { get; init; } = new();
    [Required, MaxLength(2)] public string[] AgruparPor { get; init; } = [];
    [Required] public string OrdenarPor { get; init; } = "contatos";
    [Range(1, 200)] public int Limite { get; init; } = 20;
    public override void Validate()
    {
        base.Validate();
        Filtros.Validate();
        Require(AgruparPor.All(Groups.Contains) && Metrics.Contains(OrdenarPor), "Agrupamento ou métrica inválida.");
    }
}

public sealed record ToolSearch : ValidatedRequest
{
    [Required] public Filters Filtros { get; init; } = new();
    [Range(1, 100)] public int Limite { get; init; } = 20;
    public override void Validate() { base.Validate(); Filtros.Validate(); }
}

public sealed record ChatMessage : ValidatedRequest
{
    [Required] public string Role { get; init; } = "";
    [Required, MaxLength(6000)] public string Text { get; init; } = "";
    public override void Validate() { base.Validate(); Require(Role is "user" or "model", "Papel da mensagem inválido."); }
}

public sealed record ChatRequest : ValidatedRequest
{
    [Required, MaxLength(4000)] public string Pergunta { get; init; } = "";
    [Required, MaxLength(20)] public ChatMessage[] Historico { get; init; } = [];
    public override void Validate()
    {
        base.Validate();
        foreach (var message in Historico)
        {
            Require(message is not null, "Histórico inválido.");
            message!.Validate();
        }
    }
}

public sealed record ContactPage(long Total, List<Dictionary<string, object?>> Itens);
