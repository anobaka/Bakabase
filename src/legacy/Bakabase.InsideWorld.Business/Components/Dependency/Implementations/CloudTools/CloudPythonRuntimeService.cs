using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
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
    public override string Id => "cloud-python-component-service";
    protected override string KeyInLocalizer => "CloudPython";
    public override bool IsRequired => false;
    public override bool IsAvailableOnCurrentPlatform => RuntimeInformation.OSArchitecture is Architecture.X64 or Architecture.Arm64;
    public string PythonExecutable => Path.Combine(Context.Location ?? DefaultLocation, "python",
        OperatingSystem.IsWindows() ? "python.exe" : "bin/python3");
    protected override IDiscoverer Discoverer { get; } = new PythonDiscoverer();

    public override Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct) =>
        Task.FromResult(new DependentComponentVersion { Version = PythonVersion,
            Description = $"Application-owned CPython {PythonVersion} (python-build-standalone {BuildVersion}); validated 2026-10-19." });

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
        var extracted = Path.Combine(TempDirectory, "extracted");
        Directory.CreateDirectory(extracted);
        await using (var archive = File.OpenRead(files.Single()))
        await using (var gzip = new GZipStream(archive, CompressionMode.Decompress))
            await TarFile.ExtractToDirectoryAsync(gzip, extracted, overwriteFiles: false, cancellationToken: ct);
        var source = Path.Combine(extracted, "python");
        if (!Directory.Exists(source)) throw new InvalidDataException("Python archive layout changed.");
        // A fixed, verified runtime is immutable. Never replace a runtime underneath active downloads.
        var destination = Path.Combine(DefaultLocation, "python");
        if (Directory.Exists(destination)) throw new IOException("Remove the incomplete cloud Python component before reinstalling.");
        Directory.Move(source, destination);
    }

    private sealed class PythonDiscoverer : IDiscoverer
    {
        public async Task<(string Location, string? Version)?> Discover(string defaultDirectory, CancellationToken ct)
        {
            var executable = Path.Combine(defaultDirectory, "python", OperatingSystem.IsWindows() ? "python.exe" : "bin/python3");
            if (!File.Exists(executable)) return null;
            try
            {
                var version = await CloudToolProcess.RunAsync(executable,
                    ["-I", "-c", "import platform; print(platform.python_version())"], defaultDirectory, ct);
                return version == PythonVersion ? (defaultDirectory, version) : null;
            }
            catch (Exception) when (!ct.IsCancellationRequested) { return null; }
        }
    }
}
