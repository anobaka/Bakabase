using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Models.Configs;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;

public record PythonRuntimeConfigurationViewModel(PythonRuntimeConfiguration Configuration, PythonRuntimeInfo? Runtime, string? Error = null);
public record PythonRuntimeCandidateViewModel(string Label, PythonRuntimeConfiguration Configuration);
public record PythonRuntimeValidationViewModel(bool IsValid, string? Error, PythonRuntimeInfo? Runtime,
    bool Applied, PythonRuntimeConfiguration Configuration);

/// <summary>Per-server Python source configuration. Preparation is isolated; only the final source pointer changes.</summary>
public class PythonRuntimeConfigurationService
{
    private readonly IBOptionsManager<PythonRuntimeOptions> _options;
    private readonly IServiceProvider _services;
    private readonly string _components;
    private readonly SemaphoreSlim _sourceGate = new(1, 1);
    private PythonRuntimeExecution? _current;
    private readonly object _snapshotGate = new();
    internal const string ProbeScript = "import sys,platform,ssl,venv,ensurepip,json,struct; print(json.dumps({'version':platform.python_version(),'implementation':platform.python_implementation(),'machine':platform.machine(),'bits':struct.calcsize('P')*8,'executable':sys.executable,'basePrefix':sys.base_prefix,'cacheTag':sys.implementation.cache_tag}))";
    internal const string IsolatedProbeScript = "import sys,ssl,ensurepip; assert sys.prefix != sys.base_prefix; print('isolated-ok')";

    public PythonRuntimeConfigurationService(IBOptionsManager<PythonRuntimeOptions> options, AppService app,
        IServiceProvider services) : this(options, app.ComponentsPath, services) { }

    internal PythonRuntimeConfigurationService(IBOptionsManager<PythonRuntimeOptions> options, string components,
        IServiceProvider services)
    {
        _options = options;
        _components = components;
        _services = services;
    }

    public string ManagedExecutable => Path.Combine(_components, "cloud-python", "python",
        OperatingSystem.IsWindows() ? "python.exe" : "bin/python3");
    public PythonRuntimeMode SelectedMode => CurrentConfiguration().Mode;

    public PythonRuntimeExecution Capture()
    {
        lock (_snapshotGate)
        {
            if (_current != null) return _current;
            var configuration = Normalize(_options.Value.Configuration);
            var info = configuration.Mode == PythonRuntimeMode.Managed ? ManagedInfo() : _options.Value.Runtime
                ?? throw new InvalidOperationException("The configured external Python has not been validated. Apply its configuration again.");
            return _current = new PythonRuntimeExecution(configuration, info);
        }
    }

    private PythonRuntimeInfo ManagedInfo() => new(CloudPythonRuntimeService.PythonVersion,
        RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64", ManagedExecutable,
        Fingerprint("managed", ManagedExecutable, CloudPythonRuntimeService.PythonVersion));

    internal async Task<IDisposable> LockSourceAsync(CancellationToken ct)
    {
        await _sourceGate.WaitAsync(ct);
        return new GateLease(_sourceGate);
    }
    private sealed class GateLease(SemaphoreSlim gate) : IDisposable { public void Dispose() => gate.Release(); }

    public async Task<PythonRuntimeConfigurationViewModel> GetConfigurationAsync(CancellationToken ct)
    {
        var configuration = CurrentConfiguration();
        try
        {
            var current = Capture();
            var verified = await ProbeAsync(current.Configuration, ct);
            if (verified.Info.SourceFingerprint != current.Info.SourceFingerprint)
                throw new InvalidOperationException("The selected Python changed on disk. Validate and apply its configuration again.");
            return new(configuration, current.Info);
        }
        catch (Exception e) when (!ct.IsCancellationRequested) { return new(configuration, null, e.Message); }
    }

    public IReadOnlyList<PythonRuntimeCandidateViewModel> GetCandidates()
    {
        var result = new List<PythonRuntimeCandidateViewModel> { new("Bakabase managed Python", new()) };
        if (CurrentConfiguration().Mode != PythonRuntimeMode.Managed)
            result.Add(new("Currently configured Python", CurrentConfiguration()));
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (!Path.IsPathFullyQualified(directory)) continue;
            foreach (var name in OperatingSystem.IsWindows() ? new[] { "python.exe", "python3.exe" } : new[] { "python3", "python" })
            {
                var executable = Path.Combine(directory, name);
                if (!File.Exists(executable) || !seen.Add(Path.GetFullPath(executable))) continue;
                result.Add(new(executable, new() { Mode = PythonRuntimeMode.External, PythonExecutable = executable }));
                if (result.Count >= 25) return result;
            }
        }
        return result;
    }

    public async Task<PythonRuntimeValidationViewModel> ValidateAsync(PythonRuntimeConfiguration input, CancellationToken ct)
    {
        try
        {
            var source = await ProbeAsync(Normalize(input), ct);
            await VerifyIsolatedEnvironmentAsync(source, ct);
            return new(true, null, source.Info, false, CurrentConfiguration());
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            return new(false, e.Message, null, false, CurrentConfiguration());
        }
    }

    public async Task<PythonRuntimeValidationViewModel> ApplyAsync(PythonRuntimeConfiguration input, CancellationToken ct)
    {
        var result = await PrepareAndApplyAsync(input, ct);
        if (result.Applied)
        {
            // The source lock has been released. A concurrent component install may own its
            // operation lock, so discovery must never happen while holding the source lock.
            try { await RefreshContextsAsync(); }
            catch (Exception e) { _services.GetService<ILogger<PythonRuntimeConfigurationService>>()?.LogWarning(e, "Python source was applied but dependency refresh failed."); }
        }
        return result;
    }

    private async Task<PythonRuntimeValidationViewModel> PrepareAndApplyAsync(PythonRuntimeConfiguration input, CancellationToken ct)
    {
        try
        {
            using var lease = await LockSourceAsync(ct);
            var configuration = Normalize(input);
            if (configuration.Mode == PythonRuntimeMode.Managed)
                await _services.GetRequiredService<CloudPythonRuntimeService>().EnsureManagedInstalledAsync(ct);
            var candidate = await ProbeAsync(configuration, ct);
            await VerifyIsolatedEnvironmentAsync(candidate, ct);
            PythonRuntimeExecution? old;
            try { old = Capture(); } catch (Exception) { old = null; }
            // Only tools that are already usable are prepared here. Selecting a runtime does
            // not authorize installation of other optional download tools.
            await PrepareToolsAsync(old, candidate, ct);
            ct.ThrowIfCancellationRequested();
            await _options.SaveAsync(new PythonRuntimeOptions { Configuration = configuration with { }, Runtime = candidate.Info });
            lock (_snapshotGate) _current = candidate;
            return new(true, null, candidate.Info, true, configuration with { });
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            return new(false, e.Message, null, false, CurrentConfiguration());
        }
    }

    private PythonRuntimeConfiguration CurrentConfiguration()
    {
        lock (_snapshotGate) return _current?.Configuration ?? _options.Value.Configuration with { };
    }

    protected virtual async Task PrepareToolsAsync(PythonRuntimeExecution? previous, PythonRuntimeExecution candidate, CancellationToken ct)
    {
        foreach (var tool in new CloudPythonToolService[]
            { _services.GetRequiredService<GdownService>(), _services.GetRequiredService<GalleryDlService>(), _services.GetRequiredService<PyzipperService>() })
            if (previous != null && tool.HasEnvironmentFor(previous)) await tool.PrepareForAsync(candidate, ct);
    }

    protected virtual async Task RefreshContextsAsync()
    {
        foreach (var component in new DependentComponentService[]
            { _services.GetRequiredService<CloudPythonRuntimeService>(), _services.GetRequiredService<GdownService>(), _services.GetRequiredService<GalleryDlService>(), _services.GetRequiredService<PyzipperService>() })
        {
            try { await component.Discover(CancellationToken.None); }
            catch (Exception e) { _services.GetService<ILogger<PythonRuntimeConfigurationService>>()?.LogWarning(e, "Python source was applied but dependency rediscovery failed."); }
        }
    }

    internal async Task<PythonRuntimeExecution> ProbeAsync(PythonRuntimeConfiguration configuration, CancellationToken ct)
    {
        var executable = configuration.Mode switch
        {
            PythonRuntimeMode.Managed => ManagedExecutable,
            PythonRuntimeMode.External => configuration.PythonExecutable!,
            PythonRuntimeMode.Conda => Path.Combine(configuration.CondaPrefix!, OperatingSystem.IsWindows() ? "python.exe" : "bin/python"),
            _ => throw new ArgumentException("Unknown Python runtime source.")
        };
        if (!File.Exists(executable)) throw new InvalidOperationException("The Python executable does not exist on this server. Install managed Python or choose an existing executable.");
        var launch = new PythonRuntimeExecution(configuration, new("", "", executable, ""));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        string output;
        try { output = await launch.RunAsync(["-I", "-c", ProbeScript], Path.GetDirectoryName(executable)!, timeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new InvalidOperationException("Python validation timed out after 30 seconds."); }
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        var version = root.GetProperty("version").GetString()!;
        if (root.GetProperty("implementation").GetString() != "CPython" || !Version.TryParse(version, out var parsed)
            || parsed.Major != 3 || parsed.Minor < 10)
            throw new InvalidOperationException("CPython 3.10 or later with ssl, venv and ensurepip is required.");
        if (configuration.Mode == PythonRuntimeMode.Managed && version != CloudPythonRuntimeService.PythonVersion)
            throw new InvalidOperationException("The managed Python version does not match the reviewed version.");
        var machine = root.GetProperty("machine").GetString()!.ToLowerInvariant();
        var architecture = machine is "arm64" or "aarch64" ? "arm64" : machine is "amd64" or "x86_64" ? "x64" : "unsupported";
        if (root.GetProperty("bits").GetInt32() != 64 || architecture == "unsupported")
            throw new InvalidOperationException("A 64-bit x64 or ARM64 Python runtime is required.");
        var resolved = Path.GetFullPath(root.GetProperty("executable").GetString()!);
        var file = new FileInfo(resolved);
        if (file.LinkTarget != null) file = new FileInfo(file.ResolveLinkTarget(returnFinalTarget: true)!.FullName);
        var identity = configuration.Mode == PythonRuntimeMode.Managed ? ManagedInfo().SourceFingerprint :
            Fingerprint(JsonSerializer.Serialize(configuration), resolved, file.FullName, version, architecture,
                root.GetProperty("basePrefix").GetString()!, root.GetProperty("cacheTag").GetString()!,
                file.Length.ToString(), file.LastWriteTimeUtc.Ticks.ToString());
        return new PythonRuntimeExecution(configuration, new(version, architecture, resolved, identity));
    }

    private async Task VerifyIsolatedEnvironmentAsync(PythonRuntimeExecution source, CancellationToken ct)
    {
        var directory = Path.Combine(_components, "cloud-python", "validation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            var environment = Path.Combine(directory, "venv");
            await source.RunAsync(["-I", "-m", "venv", "--copies", environment], directory, timeout.Token);
            var python = Path.Combine(environment, OperatingSystem.IsWindows() ? "Scripts/python.exe" : "bin/python");
            if (await source.WithPython(python).RunAsync(["-I", "-c", IsolatedProbeScript], directory, timeout.Token) != "isolated-ok")
                throw new InvalidOperationException("The selected Python could not create an isolated environment.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new InvalidOperationException("Creating the isolated Python environment timed out."); }
        finally { Directory.Delete(directory, true); }
    }

    internal static PythonRuntimeConfiguration Normalize(PythonRuntimeConfiguration input)
    {
        static string Full(string? path, string label, bool executable)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                throw new ArgumentException(label + " must be an absolute path on this server.");
            if (path.IndexOfAny(['\0', '\r', '\n']) >= 0) throw new ArgumentException(label + " contains invalid characters.");
            if (executable && OperatingSystem.IsWindows() && !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(label + " must be an executable .exe, not a shell or batch command.");
            return Path.GetFullPath(path);
        }
        return input.Mode switch
        {
            PythonRuntimeMode.Managed => new(),
            PythonRuntimeMode.External => new() { Mode = input.Mode, PythonExecutable = Full(input.PythonExecutable, "Python executable", true) },
            PythonRuntimeMode.Conda => new() { Mode = input.Mode, CondaExecutable = Full(input.CondaExecutable, "Conda executable", true), CondaPrefix = Full(input.CondaPrefix, "Conda environment", false) },
            _ => throw new ArgumentException("Unknown Python runtime source.")
        };
    }

    internal static string Fingerprint(params string[] parts) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\0", parts)))).ToLowerInvariant()[..24];
}
