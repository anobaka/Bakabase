namespace Bakabase.Modules.ThirdParty.ThirdParties.Av;

/// <summary>
/// Fallback provider used when no user-configured options have been wired in.
/// Returns the built-in default cookie/base-url for known sources, or all-nulls
/// for unknown ones. Always reports Enabled = true.
/// </summary>
public class DefaultAvSourceOptionsProvider : IAvSourceOptionsProvider
{
    public AvSourceResolvedConfig Resolve(string sourceId)
    {
        var key = (sourceId ?? string.Empty).ToLowerInvariant();
        AvSourceDefaults.DefaultBaseUrls.TryGetValue(key, out var baseUrl);
        AvSourceDefaults.DefaultCookies.TryGetValue(key, out var cookie);
        return new AvSourceResolvedConfig(true, baseUrl, cookie, null);
    }

    public IReadOnlyDictionary<int, IReadOnlyList<string>>? GetPreferredSourcesByTarget() => null;
}
