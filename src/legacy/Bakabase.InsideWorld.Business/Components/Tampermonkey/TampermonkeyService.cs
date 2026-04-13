using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Gui;
using AppContext = Bakabase.Infrastructures.Components.App.AppContext;

namespace Bakabase.InsideWorld.Business.Components.Tampermonkey;

public class TampermonkeyService(IGuiAdapter guiAdapter, AppContext appContext, IHttpClientFactory httpClientFactory)
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
    /// Fetches the script from CDN and replaces the default API URL with the
    /// current endpoint. The script's @updateURL/@downloadURL still point to CDN,
    /// so Tampermonkey auto-updates will work. The injected endpoint is auto-persisted
    /// via GM_setValue on first use, surviving future CDN updates.
    /// </summary>
    public async Task<string?> GetScript()
    {
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

        var serverAddress = $"{appContext.ApiEndpoint}";
        return template.Replace("const DEFAULT_API_URL = '';", $"const DEFAULT_API_URL = '{serverAddress}';");
    }
}
