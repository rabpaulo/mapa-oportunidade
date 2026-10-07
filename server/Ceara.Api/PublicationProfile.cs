namespace Ceara.Api;

public static class PublicationProfile
{
    public const string Full = "integral";
    public const string Minimized = "minimizado";
    public const string Policy = "empresas-sem-dados-pessoais-v2";
    public static bool IsPublic(IConfiguration config) => config["CEARA_PUBLIC"] == "1" || config["VERCEL"] == "1";
    public static string Get(IConfiguration config)
    {
        var profile = config["CEARA_DATA_PROFILE"]?.Trim();
        if (string.IsNullOrEmpty(profile)) return IsPublic(config) ? Minimized : Full;
        if (profile is not (Full or Minimized)) throw new InvalidOperationException("CEARA_DATA_PROFILE deve ser integral ou minimizado.");
        if (IsPublic(config) && profile != Minimized) throw new InvalidOperationException("A hospedagem pública exige o perfil minimizado v2 do Ceará.");
        return profile;
    }
}
