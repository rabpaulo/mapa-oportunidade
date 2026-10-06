namespace Ceara.Api;

public static class PublicationProfile
{
    public const string Full = "integral";
    public const string Minimized = "minimizado";

    // Hosting protections are independent of the selected dataset.
    public static string Get(IConfiguration config) => config["CEARA_DATA_PROFILE"]?.Trim() switch
    {
        null or "" => config["CEARA_PUBLIC"] == "1" ? Minimized : Full,
        Full => Full,
        Minimized => Minimized,
        _ => throw new InvalidOperationException("CEARA_DATA_PROFILE deve ser integral ou minimizado.")
    };
}
