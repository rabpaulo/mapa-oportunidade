namespace Ceara.Api;

public static class Geography
{
    public sealed record State(string Uf, string Nome, string Codigo, int TotalMunicipios);
    public static readonly State[] All = [
        new("AC","Acre","12",22), new("AL","Alagoas","27",102), new("AM","Amazonas","13",62), new("AP","Amapá","16",16),
        new("BA","Bahia","29",417), new("CE","Ceará","23",184), new("DF","Distrito Federal","53",1), new("ES","Espírito Santo","32",78),
        new("GO","Goiás","52",246), new("MA","Maranhão","21",217), new("MG","Minas Gerais","31",853), new("MS","Mato Grosso do Sul","50",79),
        new("MT","Mato Grosso","51",142), new("PA","Pará","15",144), new("PB","Paraíba","25",223), new("PE","Pernambuco","26",185),
        new("PI","Piauí","22",224), new("PR","Paraná","41",399), new("RJ","Rio de Janeiro","33",92), new("RN","Rio Grande do Norte","24",167),
        new("RO","Rondônia","11",52), new("RR","Roraima","14",15), new("RS","Rio Grande do Sul","43",497), new("SC","Santa Catarina","42",295),
        new("SE","Sergipe","28",75), new("SP","São Paulo","35",645), new("TO","Tocantins","17",139)
    ];
    public static string Resolve(string? uf, IConfiguration config)
    {
        var value = string.IsNullOrWhiteSpace(uf) ? "CE" : uf.Trim().ToUpperInvariant();
        if (!All.Any(s => s.Uf == value)) throw new ApiError("UF inválida.", 422);
        if (PublicationProfile.IsPublic(config) && value != "CE") throw new ApiError("A publicação disponibiliza somente o Ceará.", 403);
        return value;
    }
    public static State Get(string uf) => All.Single(s => s.Uf == uf);
}
