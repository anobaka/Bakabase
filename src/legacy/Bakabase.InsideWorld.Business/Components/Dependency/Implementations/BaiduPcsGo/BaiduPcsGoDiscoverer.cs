using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.BaiduPcsGo;

public sealed class BaiduPcsGoDiscoverer(ILoggerFactory loggerFactory) : IDiscoverer
{
    public async Task<(string Location, string? Version)?> Discover(string defaultDirectory, CancellationToken ct)
    {
        foreach (var location in new[] { defaultDirectory }.Concat(
                     (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)))
        {
            if (string.IsNullOrWhiteSpace(location)) continue;
            var found = await TryDiscoverAt(location, ct);
            if (found != null) return found;
        }
        return null;
    }

    internal async Task<(string Location, string? Version)?> TryDiscoverAt(string directory, CancellationToken ct)
    {
        var file = Path.Combine(directory, OperatingSystem.IsWindows() ? "bakabase-baidu-pcs.exe" : "bakabase-baidu-pcs");
        if (!File.Exists(file)) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var info = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true };
            info.ArgumentList.Add("--version");
            info.Environment["BAIDUPCS_GO_CONFIG_DIR"] = Path.GetFullPath(directory);
            using var process = Process.Start(info)!;
            using var cancel = timeout.Token.Register(() => { try { process.Kill(true); } catch { } });
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var error = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                var version = (await output).Trim(); await error;
                return process.ExitCode == 0 && version == BaiduPcsGoService.BridgeVersion ? (directory, version) : null;
            }
            finally
            {
                if (!process.HasExited) { try { process.Kill(true); } catch { } }
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        { loggerFactory.CreateLogger<BaiduPcsGoDiscoverer>().LogDebug("Skipping unavailable BaiduPCS-Go bridge at {Directory}", directory); return null; }
    }
}
