using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Infrastructures.Components.Gui;
using Bootstrap.Components.Configuration.Abstractions;
using AppContext = Bakabase.Infrastructures.Components.App.AppContext;

namespace Bakabase.InsideWorld.Business.Components.Tampermonkey;

public class TampermonkeyService(IGuiAdapter guiAdapter, AppContext appContext, IHttpClientFactory httpClientFactory,
    IBOptionsManager<AppOptions> appOptions)
{
    private const string InstallScriptUrlTemplate = "https://www.tampermonkey.net/script_installation.php#url={jsUrl}";
    public const string ScriptCdnUrl = "https://cdn-public.anobaka.com/app/bakabase/scripts/bakabase.user.js";

    /// <summary>
    /// Opens the Tampermonkey install dialog pointing to the local API endpoint,
    /// which serves the script with the current API endpoint pre-filled.
    /// </summary>
    public Task Install()
    {
        var jsUrl = $"{appContext.ApiEndpoint}/tampermonkey/script/bakabase.user.js";
        var installUrl = InstallScriptUrlTemplate.Replace("{jsUrl}", jsUrl);
        Process.Start(new ProcessStartInfo(installUrl) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    /// <summary>
    /// Fetches the script from CDN and seeds its stored API URL before it runs.
    /// The script's @updateURL/@downloadURL still point to CDN,
    /// so Tampermonkey auto-updates will work. The injected endpoint is auto-persisted
    /// via GM_setValue on first use, surviving future CDN updates.
    /// </summary>
    public async Task<string?> GetScript(string apiEndpoint)
    {
        if (!TryNormalizeOrigin(apiEndpoint, out var origin))
        {
            throw new ArgumentException("An HTTP(S) origin is required.", nameof(apiEndpoint));
        }

        using var client = httpClientFactory.CreateClient();
        string template;
        try
        {
            template = await client.GetStringAsync(ScriptCdnUrl);
        }
        catch
        {
            return null;
        }

        // Production bundlers erase an empty default constant. Insert outside the
        // compiled bundle instead, after the metadata so extension installation and
        // CDN updates keep working. Never overwrite a user's existing preferences.
        const string metadataEnd = "// ==/UserScript==";
        if (!template.StartsWith("// ==UserScript==", StringComparison.Ordinal)) return null;
        var end = template.IndexOf(metadataEnd, StringComparison.Ordinal);
        if (end < 0) return null;
        // A wildcard @connect still asks for permission when a new host is first used.
        // Declare the installed server explicitly as the intended LAN destination.
        var endpoint = new Uri(origin!);
        var connectHost = endpoint.HostNameType == UriHostNameType.IPv6 ? endpoint.Host : endpoint.IdnHost;
        var connectLine = $"// @connect      {connectHost}\n";
        if (!Regex.IsMatch(template[..end], @"(?m)^//\s*@connect\s+" + Regex.Escape(connectHost) + @"\s*$"))
        {
            template = template.Insert(end, connectLine);
            end += connectLine.Length;
        }

        var locale = AppService.NormalizeLanguageCode(appOptions.Value.Language) == "zh-CN" ? "zh" : "en";
        var bootstrap = $$"""

            // Bakabase connection bootstrap
            (() => {
              if (!GM_getValue('api_base_url', '')) {
                GM_setValue('api_base_url', {{JsonSerializer.Serialize(origin)}});
              }
              if (!GM_getValue('locale', '')) {
                GM_setValue('locale', {{JsonSerializer.Serialize(locale)}});
              }
            })();

            """;
        return template.Insert(end + metadataEnd.Length, bootstrap);
    }

    public static bool TryNormalizeOrigin(string? value, out string? origin)
    {
        origin = null;
        if (string.IsNullOrEmpty(value) || value.Length > 2048 ||
            !Regex.IsMatch(value, @"\Ahttps?://[^/?#\\\s]+/?\z", RegexOptions.IgnoreCase) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.IsWellFormedOriginalString() ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.Port <= 0 ||
            uri.HostNameType == UriHostNameType.Unknown || uri.Host is "0.0.0.0" or "[::]")
        {
            return false;
        }

        origin = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }
}
