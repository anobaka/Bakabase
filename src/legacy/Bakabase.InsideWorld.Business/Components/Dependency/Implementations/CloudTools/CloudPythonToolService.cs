using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Bakabase.InsideWorld.Business.Components.Dependency.Models;
using Bakabase.InsideWorld.Models.Configs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;

public abstract class CloudPythonToolService : DependentComponentService
{
    private readonly CloudPythonRuntimeService _python;
    private readonly PythonRuntimeConfigurationService _configuration;
    private readonly BakabaseWebProxy _proxy;
    private readonly SemaphoreSlim _environmentGate = new(1, 1);
    protected abstract string PackageName { get; }
    public abstract string ValidatedVersion { get; }
    public override bool IsRequired => false;
    public override IReadOnlyList<DependentComponentTarget> SupportedTargets => _python.SupportedTargets;
    protected override IEnumerable<Type> Dependencies => [typeof(CloudPythonRuntimeService)];
    public string PythonExecutable => CaptureExecution().PythonExecutable;
    protected override IDiscoverer Discoverer => new ToolDiscoverer(this);

    protected CloudPythonToolService(ILoggerFactory loggerFactory, AppService appService, string directory,
        CloudPythonRuntimeService python, BakabaseWebProxy proxy, IServiceProvider services)
        : this(loggerFactory, appService.ComponentsPath, directory, python, proxy, services) { }

    internal CloudPythonToolService(ILoggerFactory loggerFactory, string componentsPath, string directory,
        CloudPythonRuntimeService python, BakabaseWebProxy proxy, IServiceProvider services) : base(loggerFactory, componentsPath, directory, services)
    {
        _python = python;
        _configuration = services.GetRequiredService<PythonRuntimeConfigurationService>();
        _proxy = proxy;
    }

    public PythonRuntimeExecution CaptureExecution() => ExecutionFor(_configuration.Capture());

    /// <summary>Capture before awaiting readiness, so an apply cannot change half of a process launch.</summary>
    public async Task<PythonRuntimeExecution> CaptureReadyExecutionAsync(CancellationToken ct)
    {
        var source = _configuration.Capture();
        var execution = ExecutionFor(source);
        try
        {
            var verified = await _configuration.ProbeAsync(source.Configuration, ct);
            if (verified.Info.SourceFingerprint == source.Info.SourceFingerprint && await IsReadyExecutionAsync(execution, ct))
                return execution;
        }
        catch (Exception) when (!ct.IsCancellationRequested) { }
        throw new Bakabase.InsideWorld.Business.Components.Dependency.Exceptions.DependencyNotInstalledException(
            KeyInLocalizer, DisplayName, "Install or repair " + DisplayName + " in dependency management for the selected Python runtime.", Id);
    }

    private string Pointer(PythonRuntimeExecution source) => Path.Combine(DefaultLocation, "source-" + source.Info.SourceFingerprint + ".json");
    private string ExecutableIn(string directory) => Path.Combine(directory, OperatingSystem.IsWindows() ? "Scripts/python.exe" : "bin/python");

    internal PythonRuntimeExecution ExecutionFor(PythonRuntimeExecution source)
    {
        var pointer = Pointer(source);
        if (File.Exists(pointer))
        {
            var name = JsonSerializer.Deserialize<string>(File.ReadAllText(pointer));
            if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name || !name.StartsWith("venv-", StringComparison.Ordinal))
                throw new InvalidDataException("Invalid private Python environment pointer.");
            return source.WithPython(ExecutableIn(Path.Combine(DefaultLocation, name)));
        }
        // Existing managed installations keep working without downloading packages again.
        // External and Conda sources can never select these legacy environments.
        var legacy = Path.Combine(DefaultLocation, "venv-" + ValidatedVersion);
        return source.WithPython(ExecutableIn(source.Configuration.Mode == PythonRuntimeMode.Managed && Directory.Exists(legacy)
            ? legacy : Path.Combine(DefaultLocation, "venv-" + ValidatedVersion + "-" + source.Info.SourceFingerprint)));
    }

    internal bool HasEnvironmentFor(PythonRuntimeExecution source) => File.Exists(ExecutionFor(source).PythonExecutable);
    internal Task<bool> IsReadyForAsync(PythonRuntimeExecution source, CancellationToken ct) => IsReadyExecutionAsync(ExecutionFor(source), ct);

    internal async Task<bool> IsReadyExecutionAsync(PythonRuntimeExecution execution, CancellationToken ct)
    {
        if (!File.Exists(execution.PythonExecutable)) return false;
        try
        {
            using var probe = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probe.CancelAfter(TimeSpan.FromSeconds(30));
            var version = await execution.RunAsync(["-I", "-c", "import sys,ssl,importlib.metadata as m; import " + PackageName.Replace("-", "_") + "; import socks; assert sys.prefix != sys.base_prefix; print(m.version('" + PackageName + "'))"], DefaultLocation, probe.Token);
            return version == ValidatedVersion;
        }
        catch (Exception) when (!ct.IsCancellationRequested) { return false; }
    }

    public override Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct) =>
        Task.FromResult(new DependentComponentVersion { Version = ValidatedVersion,
            Description = $"{PackageName} {ValidatedVersion}; latest upstream documentation and tag validated 2026-10-19. Updates require adapter compatibility validation." });

    protected override async Task InstallCore(CancellationToken ct)
    {
        using var sourceLock = await _configuration.LockSourceAsync(ct);
        var source = _configuration.Capture();
        await PrepareForAsync(source, ct);
    }

    internal async Task PrepareForAsync(PythonRuntimeExecution source, CancellationToken ct)
    {
        await _environmentGate.WaitAsync(ct);
        try
        {
            if (await IsReadyForAsync(source, ct)) return;
            Directory.CreateDirectory(DefaultLocation);
            // Venvs contain absolute paths: build at their permanent unique location and publish
            // only an atomic pointer after import checks. Never rename or overwrite a live venv.
            var environment = Path.Combine(DefaultLocation, "venv-" + ValidatedVersion + "-" + source.Info.SourceFingerprint + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(environment);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(environment, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var succeeded = false;
            try
            {
                await source.RunAsync(["-I", "-m", "venv", "--copies", environment], DefaultLocation, ct);
                var execution = source.WithPython(ExecutableIn(environment));
                await execution.RunAsync(["-I", "-m", "pip", "--isolated", "install", "--disable-pip-version-check", "--no-input", "--no-cache-dir",
                    "--index-url", "https://pypi.org/simple", "--only-binary=:all:", PackageName + "==" + ValidatedVersion, "requests[socks]"], DefaultLocation, ct,
                    start => CloudToolProxy.ApplyToEnvironment(start, _proxy, new Uri("https://pypi.org")));
                if (!await IsReadyExecutionAsync(execution, ct)) throw new InvalidOperationException("The isolated " + PackageName + " installation did not pass verification.");
                ct.ThrowIfCancellationRequested();
                var pointer = Pointer(source);
                var temporary = pointer + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(Path.GetFileName(environment)), ct);
                    File.Move(temporary, pointer, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                succeeded = true;
            }
            finally { if (!succeeded) Directory.Delete(environment, true); }
        }
        finally { _environmentGate.Release(); }
    }

    private sealed class ToolDiscoverer(CloudPythonToolService tool) : IDiscoverer
    {
        public async Task<(string Location, string? Version)?> Discover(string defaultDirectory, CancellationToken ct)
        {
            try
            {
                var source = tool._configuration.Capture();
                var verified = await tool._configuration.ProbeAsync(source.Configuration, ct);
                return verified.Info.SourceFingerprint == source.Info.SourceFingerprint && await tool.IsReadyForAsync(source, ct)
                    ? (defaultDirectory, tool.ValidatedVersion) : null;
            }
            catch (Exception) when (!ct.IsCancellationRequested) { return null; }
        }
    }
}

public sealed class GdownService(ILoggerFactory loggerFactory, AppService appService, CloudPythonRuntimeService python,
    BakabaseWebProxy proxy, IServiceProvider services) : CloudPythonToolService(loggerFactory, appService, "gdown", python, proxy, services)
{
    public override string Id => "gdown-component-service";
    protected override string KeyInLocalizer => "Gdown";
    protected override string PackageName => "gdown";
    public override string ValidatedVersion => "6.4.2";
}

public sealed class GalleryDlService(ILoggerFactory loggerFactory, AppService appService, CloudPythonRuntimeService python,
    BakabaseWebProxy proxy, IServiceProvider services) : CloudPythonToolService(loggerFactory, appService, "gallery-dl", python, proxy, services)
{
    public override string Id => "gallery-dl-component-service";
    protected override string KeyInLocalizer => "GalleryDl";
    protected override string PackageName => "gallery-dl";
    public override string ValidatedVersion => "1.32.17";
}
