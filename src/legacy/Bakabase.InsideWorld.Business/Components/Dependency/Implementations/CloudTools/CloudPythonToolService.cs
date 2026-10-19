using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;

public abstract class CloudPythonToolService : DependentComponentService
{
    private readonly CloudPythonRuntimeService _python;
    private readonly BakabaseWebProxy _proxy;
    protected abstract string PackageName { get; }
    public abstract string ValidatedVersion { get; }
    public override bool IsRequired => false;
    public override bool IsAvailableOnCurrentPlatform => _python.IsAvailableOnCurrentPlatform;
    protected override IEnumerable<Type> Dependencies => [typeof(CloudPythonRuntimeService)];
    public string PythonExecutable => Path.Combine(DefaultLocation, "venv-" + ValidatedVersion,
        OperatingSystem.IsWindows() ? "Scripts/python.exe" : "bin/python");
    protected override IDiscoverer Discoverer => new ToolDiscoverer(this);

    protected CloudPythonToolService(ILoggerFactory loggerFactory, AppService appService, string directory,
        CloudPythonRuntimeService python, BakabaseWebProxy proxy, IServiceProvider services) : base(loggerFactory, appService, directory, services)
    {
        _python = python;
        _proxy = proxy;
    }

    public override Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct) =>
        Task.FromResult(new DependentComponentVersion { Version = ValidatedVersion,
            Description = $"{PackageName} {ValidatedVersion}; latest upstream documentation and tag validated 2026-10-19. Updates require adapter compatibility validation." });

    protected override async Task InstallCore(CancellationToken ct)
    {
        await _python.EnsureReadyAsync(ct);
        Directory.CreateDirectory(DefaultLocation);
        var environment = Path.GetDirectoryName(Path.GetDirectoryName(PythonExecutable))!;
        // Every version owns its environment. Re-running repairs an interrupted package installation.
        await CloudToolProcess.RunAsync(_python.PythonExecutable, ["-I", "-m", "venv", "--copies", environment], DefaultLocation, ct);
        await CloudToolProcess.RunAsync(PythonExecutable,
            ["-I", "-m", "pip", "--isolated", "install", "--disable-pip-version-check", "--no-input", "--no-cache-dir",
                "--index-url", "https://pypi.org/simple", "--only-binary=:all:", PackageName + "==" + ValidatedVersion, "requests[socks]"], DefaultLocation, ct,
            start => CloudToolProxy.ApplyToEnvironment(start, _proxy, new Uri("https://pypi.org")));
    }

    private sealed class ToolDiscoverer(CloudPythonToolService tool) : IDiscoverer
    {
        public async Task<(string Location, string? Version)?> Discover(string defaultDirectory, CancellationToken ct)
        {
            if (!File.Exists(tool.PythonExecutable)) return null;
            try
            {
                var version = await CloudToolProcess.RunAsync(tool.PythonExecutable,
                    ["-I", "-c", "import importlib.metadata as m; import " + tool.PackageName.Replace("-", "_") + "; import socks; print(m.version('" + tool.PackageName + "'))"], defaultDirectory, ct);
                return version == tool.ValidatedVersion ? (defaultDirectory, version) : null;
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
