using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Bakabase.Tests.SingleInstance;

/// <summary>
/// A second process running the real single-instance code (Bakabase.Tests.InstanceProbe),
/// because a lock taken twice by one process proves nothing about two.
/// </summary>
internal sealed class InstanceProbe : IDisposable
{
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(30);

    private readonly Process _process;

    private InstanceProbe(Process process, string firstLine)
    {
        _process = process;
        FirstLine = firstLine;
    }

    /// <summary>The probe's answer: <c>LOCK &lt;status&gt;</c> or <c>ENTRY &lt;result&gt;</c>.</summary>
    public string FirstLine { get; }

    public int Id => _process.Id;

    public bool HasExited => _process.HasExited;

    /// <summary>
    /// Starts the probe with <paramref name="verb"/> (<c>hold</c> or <c>enter</c>) on
    /// <paramref name="directory"/>, or <c>launch</c> with none, and waits for its first line.
    /// </summary>
    /// <param name="environment">
    /// Variables to set (or, with a null value, remove) in the probe's environment — the data
    /// directory variable for <c>launch</c>, a different <c>TMPDIR</c>.
    /// </param>
    public static async Task<InstanceProbe> StartAsync(string verb, string? directory,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        // Run with the test assembly's own dependency manifest: it lists the probe (a project
        // reference) and everything the probe needs, wherever the build put them.
        var baseDir = AppContext.BaseDirectory;
        var testAssembly = typeof(InstanceProbe).Assembly.GetName().Name!;
        var info = new ProcessStartInfo(DotnetHost())
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = baseDir,
        };
        info.ArgumentList.Add("exec");
        info.ArgumentList.Add("--runtimeconfig");
        info.ArgumentList.Add(Path.Combine(baseDir, testAssembly + ".runtimeconfig.json"));
        info.ArgumentList.Add("--depsfile");
        info.ArgumentList.Add(Path.Combine(baseDir, testAssembly + ".deps.json"));
        info.ArgumentList.Add(Path.Combine(baseDir, "Bakabase.Tests.InstanceProbe.dll"));
        info.ArgumentList.Add(verb);
        if (directory != null)
        {
            info.ArgumentList.Add(directory);
        }

        foreach (var (name, value) in environment ?? new Dictionary<string, string?>())
        {
            if (value == null)
            {
                info.Environment.Remove(name);
            }
            else
            {
                info.Environment[name] = value;
            }
        }

        var process = Process.Start(info) ?? throw new InvalidOperationException("Probe did not start.");
        var read = process.StandardOutput.ReadLineAsync();
        if (await Task.WhenAny(read, Task.Delay(AnswerTimeout)) != read || read.Result == null)
        {
            var stderr = process.HasExited ? await process.StandardError.ReadToEndAsync() : "(still running)";
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw new InvalidOperationException($"Probe gave no answer. stderr: {stderr}");
        }

        return new InstanceProbe(process, read.Result);
    }

    /// <summary>The next line of output, or null when none arrives in time.</summary>
    public async Task<string?> ReadLineAsync(TimeSpan timeout)
    {
        var read = _process.StandardOutput.ReadLineAsync();
        return await Task.WhenAny(read, Task.Delay(timeout)) == read ? read.Result : null;
    }

    /// <summary>SIGKILL on Unix, TerminateProcess on Windows: no chance to clean up.</summary>
    public void KillHard()
    {
        _process.Kill(entireProcessTree: true);
        _process.WaitForExit(10_000);
    }

    /// <summary>Closes the probe's stdin, which it takes as "let go and exit".</summary>
    public void Stop()
    {
        if (_process.HasExited) return;
        _process.StandardInput.Close();
        if (!_process.WaitForExit(10_000))
        {
            KillHard();
        }
    }

    private static string DotnetHost()
    {
        // The host that is running us: {dotnet root}/shared/Microsoft.NETCore.App/{version}/.
        var fromRuntime = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..",
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        if (File.Exists(fromRuntime))
        {
            return fromRuntime;
        }

        return Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
    }

    public void Dispose()
    {
        try
        {
            // Let it go the way an instance does, so it removes its channel's socket: that lives
            // in the user's own temporary directory now, not in a test TMPDIR that is thrown away.
            Stop();
        }
        catch
        {
            // Already gone.
        }

        _process.Dispose();
    }
}
