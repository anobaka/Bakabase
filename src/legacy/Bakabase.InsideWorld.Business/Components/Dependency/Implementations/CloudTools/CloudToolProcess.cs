using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;

/// <summary>No shell, user site packages, interactive input or inherited Python search path.</summary>
public static class CloudToolProcess
{
    public static ProcessStartInfo StartInfo(string executable, IEnumerable<string> arguments, string workingDirectory)
    {
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        foreach (var name in new[] { "PYTHONPATH", "PYTHONHOME", "PYTHONSTARTUP", "PYTHONUSERBASE", "VIRTUAL_ENV" })
            info.Environment.Remove(name);
        info.Environment["PYTHONNOUSERSITE"] = "1";
        info.Environment["PYTHONUTF8"] = "1";
        var nullDevice = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        info.Environment["PIP_CONFIG_FILE"] = nullDevice;
        info.Environment["NETRC"] = nullDevice;
        return info;
    }

    public static async Task<string> RunAsync(string executable, IEnumerable<string> arguments,
        string directory, CancellationToken ct, Action<ProcessStartInfo>? configure = null)
    {
        var start = StartInfo(executable, arguments, directory);
        configure?.Invoke(start);
        return await RunAsync(start, ct);
    }

    public static async Task<string> RunAsync(ProcessStartInfo start, CancellationToken ct)
    {
        using var process = new Process { StartInfo = start };
        process.Start();
        using var registration = ct.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception) { }
        });
        process.StandardInput.Close();
        var outputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        // Always drain stderr, but don't leak credentials, signed URLs or package index credentials to logs.
        var errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try { await process.WaitForExitAsync(ct); }
        finally
        {
            // Killing is asynchronous. Do not let a cancelled installer clean its private
            // extraction/venv while Python or pip still owns files in that directory.
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception e) when (e is InvalidOperationException or Win32Exception) { }
            }
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(outputTask, errorTask);
        }
        var output = await outputTask;
        await errorTask;
        if (process.ExitCode != 0) throw new InvalidOperationException($"Cloud tool process failed (exit {process.ExitCode}).");
        return output.Trim();
    }
}
