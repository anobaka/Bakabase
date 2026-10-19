using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.BaiduPcsGo;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.CloudDownloads.Baidu;

public sealed class BaiduPcsBridgeClient(BaiduPcsGoService dependency, BakabaseWebProxy proxy)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<JsonElement> ExecuteAsync(string configDirectory, object request,
        Func<JsonElement, Task>? progress, CancellationToken ct)
    {
        await dependency.EnsureReadyAsync(ct);
        var info = new ProcessStartInfo(dependency.Executable)
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = configDirectory
        };
        info.Environment["BAIDUPCS_GO_CONFIG_DIR"] = configDirectory;
        CloudToolProxy.ApplyToEnvironment(info, proxy.ForThirdParty(ThirdPartyId.BaiduNetdisk),
            new Uri("https://pan.baidu.com"));
        using var process = Process.Start(info) ?? throw new CloudProviderException("BAIDU_BRIDGE_START_FAILED");
        using var cancel = ct.Register(() => { try { process.Kill(true); } catch { } });
        var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, Json).AsMemory(), ct);
            process.StandardInput.Close();
            JsonElement? result = null;
            string? error = null;
            var mayHaveSideEffects = true;
            while (await process.StandardOutput.ReadLineAsync(ct) is { } line)
            {
                using var parsed = JsonDocument.Parse(line);
                var value = parsed.RootElement;
                switch (value.GetProperty("type").GetString())
                {
                    case "progress" when progress != null: await progress(value); break;
                    case "result": result = value.Clone(); break;
                    case "error":
                        error = value.GetProperty("code").GetString();
                        mayHaveSideEffects = !value.TryGetProperty("mayHaveSideEffects", out var effects) || effects.GetBoolean();
                        break;
                }
            }
            await process.WaitForExitAsync(ct); await stderr;
            if (process.ExitCode != 0 || error != null || result == null)
            {
                var code = error is { } safe && safe.StartsWith("BAIDU_", StringComparison.Ordinal) && safe.Length < 100
                    ? safe : "BAIDU_BRIDGE_FAILED";
                throw new CloudProviderException(code, mayHaveSideEffects);
            }
            return result.Value;
        }
        finally
        {
            if (!process.HasExited) { try { process.Kill(true); } catch { } }
            await process.WaitForExitAsync(CancellationToken.None); await stderr;
        }
    }
}
