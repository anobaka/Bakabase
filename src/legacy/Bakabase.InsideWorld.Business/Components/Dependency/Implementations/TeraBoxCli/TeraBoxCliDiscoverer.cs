using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.TeraBoxCli;

public sealed class TeraBoxCliDiscoverer(ILoggerFactory loggerFactory) : IDiscoverer
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
        var executable = Path.Combine(directory, OperatingSystem.IsWindows() ? "terabox.exe" : "terabox");
        if (!File.Exists(executable)) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            // Cobra handles --version before command hooks; never initializes a user profile.
            info.ArgumentList.Add("--no-check-update");
            info.ArgumentList.Add("--version");
            using var process = Process.Start(info)!;
            process.StandardInput.Close();
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
        { loggerFactory.CreateLogger<TeraBoxCliDiscoverer>().LogDebug("Skipping unavailable TeraBox CLI executable."); return null; }
    }

    internal static string? ReadVersion(string output)
    {
        var match = Regex.Match(output, @"\Aterabox (\d+\.\d+\.\d+)(?:\r?\n)?\z");
        return match.Success && System.Version.TryParse(match.Groups[1].Value, out var version) &&
               version == new Version(1, 1, 0) ? match.Groups[1].Value : null;
    }
}
