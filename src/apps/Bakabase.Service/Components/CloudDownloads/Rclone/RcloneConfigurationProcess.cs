using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Rclone;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Service.Components.CloudDownloads.Rclone;

internal interface IRcloneConfigurationProcess
{
    Task<JsonElement> ExecuteAsync(string directory, string method, object input, CancellationToken ct);
}

/// <summary>Native rclone configuration calls, without listeners or secret command-line arguments.</summary>
internal sealed class RcloneConfigurationProcess : IRcloneConfigurationProcess
{
    private readonly Func<CancellationToken, Task<string>> _executable;
    private readonly BakabaseWebProxy _proxy;
    public RcloneConfigurationProcess(RcloneService dependency, BakabaseWebProxy proxy)
        : this(async ct => { await dependency.EnsureReadyAsync(ct); return dependency.Executable; }, proxy) { }
    internal RcloneConfigurationProcess(Func<CancellationToken, Task<string>> executable, BakabaseWebProxy proxy)
    { _executable = executable; _proxy = proxy; }

    public async Task<JsonElement> ExecuteAsync(string directory, string method, object input, CancellationToken ct)
    {
        if (method is not ("config/create" or "config/update" or "config/get"))
            throw new RcloneRemoteException("RCLONE_CONFIGURATION_OPERATION_INVALID");
        var executable = await _executable(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var start = Start(executable, directory, method, input, _proxy);
        string text;
        try { text = await RcloneProcess.RunAsync(start, null, null, timeout.Token,
            error => RcloneConfigurationErrors.Classify(error, "RCLONE_REMOTE_FAILED")); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new RcloneRemoteException("RCLONE_CONFIGURATION_TIMED_OUT"); }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception)
        { throw new RcloneRemoteException("RCLONE_CONFIGURATION_PROCESS_FAILED"); }
        if (method != "config/get") await PersistParametersAsync(directory, input, ct);
        if (text.Length > 1024 * 1024) throw new RcloneRemoteException("RCLONE_CONFIGURATION_OUTPUT_INVALID");
        try
        {
            using var document = JsonDocument.Parse(text);
            if (method == "config/get")
            {
                if (!document.RootElement.TryGetProperty("managed", out var profile))
                    throw new RcloneRemoteException("RCLONE_CONFIGURATION_OUTPUT_INVALID");
                return profile.Clone();
            }
            return document.RootElement.Clone();
        }
        catch (JsonException) { throw new RcloneRemoteException("RCLONE_CONFIGURATION_OUTPUT_INVALID"); }
    }

    // Environment overrides are consumed by native OAuth, but rclone does not save them.
    // Keep the same application in its private staging configuration for subsequent native
    // questions and refresh. No credential ever needs to become a process argument.
    internal static async Task PersistParametersAsync(string directory, object input, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(input));
        if (!document.RootElement.TryGetProperty("parameters", out var parameters)) return;
        var keys = new[] {"client_id", "client_secret", "auth_url", "token_url"};
        var values = keys.Where(key => parameters.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
            .ToDictionary(key => key, key => parameters.GetProperty(key).GetString()!);
        if (values.Count == 0) return;
        if (values.Values.Any(value => value.Any(char.IsControl))) throw new RcloneRemoteException("RCLONE_CONFIGURATION_OUTPUT_INVALID");
        var path = Path.Combine(directory, "rclone.conf"); RcloneRemoteEngine.RejectLinks(path);
        var lines = await File.ReadAllLinesAsync(path, ct);
        var preserved = lines.Where(line => !values.Keys.Any(key => line.TrimStart().StartsWith(key + " =", StringComparison.Ordinal)));
        await RclonePrivateStorage.WriteAsync(path, string.Join('\n', preserved) + "\n" +
            string.Join('\n', values.Select(value => value.Key + " = " + value.Value)) + "\n", ct);
    }

    internal static ProcessStartInfo Start(string executable, string directory, string method, object input, BakabaseWebProxy proxy)
    {
        RclonePrivateStorage.Directory(directory);
        var config = Path.Combine(directory, "rclone.conf"); RcloneRemoteEngine.RejectLinks(config);
        var start = new ProcessStartInfo(executable) {UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = directory, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true};
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("RCLONE_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        using (var document = JsonDocument.Parse(JsonSerializer.Serialize(input)))
            if (document.RootElement.TryGetProperty("parameters", out var parameters))
                foreach (var key in new[] {"client_id", "client_secret", "auth_url", "token_url"})
                    if (parameters.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                        start.Environment["RCLONE_CONFIG_MANAGED_" + key.ToUpperInvariant()] = value.GetString();
        // Config's --result/--state string flags consume these environment variables. Do not use
        // RCLONE_JSON: other rclone commands register a conflicting global boolean --json flag.
        // The only command arguments containing values are our own non-secret defaults and paths.
        start.ArgumentList.Add("config");
        if (method == "config/get") start.ArgumentList.Add("dump");
        else if (method is "config/create" or "config/update")
        {
            start.ArgumentList.Add(method == "config/create" ? "create" : "update");
            start.ArgumentList.Add("managed");
            if (method == "config/create") start.ArgumentList.Add("onedrive");
            start.ArgumentList.Add("config_is_local=false");
            start.ArgumentList.Add("access_scopes=" + RcloneAuthorizationService.ReadScopes);
            start.ArgumentList.Add("--non-interactive");
            if (method == "config/update")
            {
                using var document = JsonDocument.Parse(JsonSerializer.Serialize(input));
                var options = document.RootElement.GetProperty("opt");
                start.Environment["RCLONE_STATE"] = options.GetProperty("state").GetString()!;
                start.Environment["RCLONE_RESULT"] = options.GetProperty("result").GetString()!;
                start.ArgumentList.Add("--continue");
            }
        }
        else throw new RcloneRemoteException("RCLONE_CONFIGURATION_OPERATION_INVALID");
        foreach (var arg in new[] {"--config", config, "--cache-dir", Path.Combine(directory, "cache"),
                     "--temp-dir", Path.Combine(directory, "temporary"), "--ask-password=false", "--log-level", "ERROR"})
            start.ArgumentList.Add(arg);
        CloudToolProxy.ApplyToEnvironment(start, proxy.ForDownloadTool(DownloadToolProxyTargets.Rclone, ThirdPartyId.OneDrive),
            new Uri("https://graph.microsoft.com"));
        return start;
    }
}
