using System.Diagnostics;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.Storage.Services;
using Bakabase.InsideWorld.Business.Components.Tampermonkey.Models.Constants;
using AppContext = Bakabase.Infrastructures.Components.App.AppContext;

namespace Bakabase.InsideWorld.Business.Components.Tampermonkey;

public class TampermonkeyService(IGuiAdapter guiAdapter, AppContext appContext)
{
    private static readonly string ScriptPath =
        Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "Components", "Tampermonkey", "Scripts");

    /// <summary>
    /// Tampermonkey does not handle url encoding properly, so do not encode jsUrl in most cases.
    /// </summary>
    private const string InstallScriptUrlTemplate = "https://www.tampermonkey.net/script_installation.php#url={jsUrl}";

    private static async Task<string> GetScriptTemplate(TampermonkeyScript script)
    {
        return await File.ReadAllTextAsync(Path.Combine(ScriptPath, $"{script.ToString().ToLower()}.tpl.js"));
    }

    public async Task Install(TampermonkeyScript script)
    {
        var serverAddress = $"{appContext.ServerAddresses.First(x => !x.Contains("0.0.0.0"))}";
        var jsUrl = $"{serverAddress}/tampermonkey/script/{(int)script}.user.js";
        var installUrl = InstallScriptUrlTemplate.Replace("{jsUrl}", jsUrl);
        Process.Start(new ProcessStartInfo(installUrl) { UseShellExecute = true });
    }

    public async Task<string> GetScript(TampermonkeyScript script)
    {
        var template = await GetScriptTemplate(script);
        var serverAddress = $"{appContext.ServerAddresses.First(x => !x.Contains("0.0.0.0"))}";
        return template.Replace("{appEndpoint}", serverAddress);
    }
}