using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Rclone;

public sealed class RcloneDiscoverer(ILoggerFactory loggerFactory) : IDiscoverer
{
    public async Task<(string Location, string? Version)?> Discover(string defaultDirectory, CancellationToken ct)
    {
        foreach (var directory in new[] { defaultDirectory }.Concat((Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator)).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct())
        {
            var result = await TryDiscoverAt(OperatingSystem.IsWindows() ? directory.Trim('"') : directory, ct);
            if (result != null) return result;
        }
        return null;
    }

    internal async Task<(string Location, string? Version)?> TryDiscoverAt(string directory, CancellationToken ct)
    {
        var executable = Path.Combine(directory, OperatingSystem.IsWindows() ? "rclone.exe" : "rclone");
        if (!File.Exists(executable)) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var key in info.Environment.Keys.Where(k => k.StartsWith("RCLONE_", StringComparison.OrdinalIgnoreCase)).ToArray())
                info.Environment.Remove(key);
            foreach (var argument in new[] { "version", "--config", OperatingSystem.IsWindows() ? "NUL" : "/dev/null", "--ask-password=false" })
                info.ArgumentList.Add(argument);
            using var process = Process.Start(info)!;
            void Kill() { try { if (!process.HasExited) process.Kill(true); }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { } }
            using var cancel = timeout.Token.Register(Kill);
            var error = process.StandardError.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
            var output = new StringBuilder(); var buffer = new char[4096];
            try
            {
                int read;
                while ((read = await process.StandardOutput.ReadAsync(buffer, timeout.Token)) != 0)
                {
                    if (output.Length + read > 65536) { Kill(); return null; }
                    output.Append(buffer, 0, read);
                }
                await process.WaitForExitAsync(timeout.Token); await error;
                var version = process.ExitCode == 0 ? ReadVersion(output.ToString()) : null;
                return version == null ? null : (directory, version);
            }
            finally { Kill(); await process.WaitForExitAsync(CancellationToken.None); await error; }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        { loggerFactory.CreateLogger<RcloneDiscoverer>().LogDebug("Skipping unavailable rclone executable."); return null; }
    }

    internal static string? ReadVersion(string output)
    {
        var match = Regex.Match(output, @"\Arclone v(\d+\.\d+\.\d+)(?:\r?\n|\z)");
        return match.Success && System.Version.TryParse(match.Groups[1].Value, out var version) &&
               version >= new Version(1, 75, 2) ? match.Groups[1].Value : null;
    }
}
