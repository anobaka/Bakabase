using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.Modules.ThirdParty.ThirdParties.Av;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.Configurations;

/// <summary>
/// Resolves a per-source effective config by overlaying user-saved <see cref="AvSourceOptions"/>
/// on top of the built-in defaults from <see cref="AvSourceDefaults"/>.
/// </summary>
public class AvSourceOptionsProvider(IBOptions<AvSourceOptions> options) : IAvSourceOptionsProvider
{
    public AvSourceResolvedConfig Resolve(string sourceId)
    {
        var key = (sourceId ?? string.Empty).ToLowerInvariant();
        AvSourceDefaults.DefaultBaseUrls.TryGetValue(key, out var defaultBaseUrl);
        AvSourceDefaults.DefaultCookies.TryGetValue(key, out var defaultCookie);

        AvSourceConfig? userConfig = null;
        options.Value.Sources?.TryGetValue(key, out userConfig);

        var enabled = userConfig?.Enabled ?? true;
        var baseUrl = !string.IsNullOrWhiteSpace(userConfig?.BaseUrl) ? userConfig!.BaseUrl : defaultBaseUrl;
        var cookie = !string.IsNullOrWhiteSpace(userConfig?.Cookie) ? userConfig!.Cookie : defaultCookie;
        var userAgent = !string.IsNullOrWhiteSpace(userConfig?.UserAgent) ? userConfig!.UserAgent : null;

        return new AvSourceResolvedConfig(enabled, baseUrl, cookie, userAgent);
    }
}
