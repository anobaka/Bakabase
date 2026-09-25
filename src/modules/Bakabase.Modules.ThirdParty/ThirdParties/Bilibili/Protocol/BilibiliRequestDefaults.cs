namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;

/// <summary>Headers Bilibili's API expects from its own web player.</summary>
public static class BilibiliRequestDefaults
{
    public const string Referer = "https://www.bilibili.com/";
    public const string Origin = "https://www.bilibili.com";

    /// <summary>
    /// Adds <c>Referer</c> and <c>Origin</c> when absent (a user-configured Referer wins). Runs after the
    /// options-based headers, so it never duplicates them.
    /// </summary>
    public static void ApplyApiDefaults(HttpRequestMessage request)
    {
        request.Headers.Referrer ??= new Uri(Referer);
        if (!request.Headers.Contains("Origin"))
        {
            request.Headers.TryAddWithoutValidation("Origin", Origin);
        }
    }
}
