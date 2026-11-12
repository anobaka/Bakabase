using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Bakabase.InsideWorld.Business.Components.Dependency.Models;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Models;
using Bakabase.InsideWorld.Models.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;

/// <summary>Optional application-owned CPython; never modifies the user's Python or global packages.</summary>
public sealed class CloudPythonRuntimeService(ILoggerFactory loggerFactory, AppService appService,
    IHttpClientFactory httpClientFactory, IServiceProvider services)
    : HttpSourceComponentService(loggerFactory, appService, "cloud-python", httpClientFactory, services)
{
    public const string PythonVersion = "3.13.16";
    public const string BuildVersion = "20261013";
    private string? _digest;
    private readonly SemaphoreSlim _managedGate = new(1, 1);
    private PythonRuntimeConfigurationService Configuration => services.GetRequiredService<PythonRuntimeConfigurationService>();
    public override string Id => "cloud-python-component-service";
    protected override string KeyInLocalizer => "CloudPython";
    public override bool IsRequired => false;
    // Selected sources may have different versions; this metadata is local and cheap.
    protected override TimeSpan LatestVersionCacheDuration => TimeSpan.Zero;
    protected override TimeSpan LatestVersionFailureCacheDuration => TimeSpan.Zero;
    public override bool CanAutoInstallOnCurrentPlatform => Configuration.SelectedMode == PythonRuntimeMode.Managed && base.CanAutoInstallOnCurrentPlatform;
    public override IReadOnlyList<DependentComponentTarget> SupportedTargets => DependentComponentPlatforms.CloudPython;
    public string PythonExecutable => Configuration.Capture().PythonExecutable;
    protected override IDiscoverer Discoverer => new PythonDiscoverer(Configuration);

    public override Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct) =>
        Task.FromResult(Configuration.Capture().Configuration.Mode == PythonRuntimeMode.Managed
            ? new DependentComponentVersion { Version = PythonVersion,
                Description = $"Application-owned CPython {PythonVersion} (python-build-standalone {BuildVersion}); validated 2026-10-19." }
            : new DependentComponentVersion { Version = Configuration.Capture().Info.PythonVersion,
                Description = "Using the selected external Python. Bakabase does not update or modify this base environment." });

    protected override async Task InstallCore(CancellationToken ct)
    {
        using var sourceLock = await Configuration.LockSourceAsync(ct);
        var current = Configuration.Capture();
        if (current.Configuration.Mode == PythonRuntimeMode.Managed) await EnsureManagedInstalledAsync(ct, reportProgress: true);
        else await Configuration.ProbeAsync(current.Configuration, ct);
    }

    internal async Task EnsureManagedInstalledAsync(CancellationToken ct, bool reportProgress = false)
    {
        await _managedGate.WaitAsync(ct);
        try
        {
            try { await ValidateRuntime(Path.Combine(DefaultLocation, "python"), ct); return; }
            catch (Exception) when (!ct.IsCancellationRequested) { }
            var urls = await GetDownloadUrls(new DependentComponentVersion { Version = PythonVersion }, ct);
            Directory.CreateDirectory(TempDirectory);
            var files = new List<string>();
            foreach (var (url, name) in urls)
            {
                await services.GetRequiredService<IHttpDownloader>().DownloadAsync(new HttpDownloadRequest(url, TempDirectory)
                { FileName = name, HttpClientName = InternalOptions.HttpClientNames.Default },
                    async (percentage, _) => { if (reportProgress) await UpdateContext(c => c.InstallationProgress = percentage * 90 / 100); }, ct);
                files.Add(Path.Combine(TempDirectory, name));
            }
            await PostDownloading(files, ct);
        }
        finally { _managedGate.Release(); }
    }

    protected override async Task<Dictionary<string, string>> GetDownloadUrls(DependentComponentVersion version, CancellationToken ct)
    {
        var target = (OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), RuntimeInformation.OSArchitecture) switch
        {
            (true, _, Architecture.X64) => "x86_64-pc-windows-msvc",
            (true, _, Architecture.Arm64) => "aarch64-pc-windows-msvc",
            (_, true, Architecture.X64) => "x86_64-apple-darwin",
            (_, true, Architecture.Arm64) => "aarch64-apple-darwin",
            (_, _, Architecture.X64) => "x86_64-unknown-linux-gnu",
            (_, _, Architecture.Arm64) => "aarch64-unknown-linux-gnu",
            _ => throw new PlatformNotSupportedException("Cloud Python requires x64 or arm64.")
        };
        var name = $"cpython-{PythonVersion}+{BuildVersion}-{target}-install_only.tar.gz";
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.github.com/repos/astral-sh/python-build-standalone/releases/tags/{BuildVersion}");
        request.Headers.UserAgent.ParseAdd("Bakabase/1.0");
        using var response = await HttpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var release = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var asset = release.RootElement.GetProperty("assets").EnumerateArray().Single(a => a.GetProperty("name").GetString() == name);
        _digest = asset.GetProperty("digest").GetString();
        if (_digest == null || !_digest.StartsWith("sha256:", StringComparison.Ordinal))
            throw new InvalidOperationException("The Python release has no SHA-256 digest.");
        return new Dictionary<string, string> { [asset.GetProperty("browser_download_url").GetString()!] = name };
    }

    protected override async Task PostDownloading(List<string> files, CancellationToken ct)
    {
        await using (var archive = File.OpenRead(files.Single()))
        {
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(archive, ct));
            if (!hash.Equals(_digest![7..], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Python download SHA-256 does not match the official release digest.");
        }
        await CloudPythonRuntimeInstaller.InstallAsync(files.Single(), DefaultLocation, TempDirectory,
            ValidateRuntime, ct);
    }

    private static async Task ValidateRuntime(string runtimeDirectory, CancellationToken ct)
    {
        using var probe = CancellationTokenSource.CreateLinkedTokenSource(ct);
        probe.CancelAfter(TimeSpan.FromSeconds(30));
        var executable = Path.Combine(runtimeDirectory, OperatingSystem.IsWindows() ? "python.exe" : "bin/python3");
        var version = await CloudToolProcess.RunAsync(executable,
            ["-I", "-c", "import platform, ssl, venv, ensurepip; print(platform.python_version())"], runtimeDirectory, probe.Token);
        if (version != PythonVersion) throw new InvalidDataException("Cloud Python runtime verification failed.");
    }

    private sealed class PythonDiscoverer(PythonRuntimeConfigurationService configuration) : IDiscoverer
    {
        public async Task<(string Location, string? Version)?> Discover(string defaultDirectory, CancellationToken ct)
        {
            try
            {
                var captured = configuration.Capture();
                var verified = await configuration.ProbeAsync(captured.Configuration, ct);
                if (verified.Info.SourceFingerprint != captured.Info.SourceFingerprint) return null;
                return (Path.GetDirectoryName(verified.Info.Executable)!, verified.Info.PythonVersion);
            }
            catch (Exception) when (!ct.IsCancellationRequested) { return null; }
        }
    }
}
