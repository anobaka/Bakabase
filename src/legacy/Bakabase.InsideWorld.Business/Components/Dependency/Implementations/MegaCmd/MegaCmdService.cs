using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Bakabase.InsideWorld.Business.Components.Dependency.Models;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.MegaCmd;

public sealed class MegaCmdService(ILoggerFactory loggerFactory, AppService appService,
    IHttpClientFactory clients, IServiceProvider services)
    : DependentComponentService(loggerFactory, appService, "megacmd", services)
{
    public override string Id => "megacmd-component-service";
    protected override string KeyInLocalizer => "MegaCmd";
    public override bool IsRequired => false;
    public override IReadOnlyList<DependentComponentTarget> SupportedTargets => DependentComponentPlatforms.MegaCmd;
    protected override IDiscoverer Discoverer { get; } = new MegaCmdDiscoverer(loggerFactory);
    public string Executable => GetExecutableWithValidation(OperatingSystem.IsWindows() ? "MEGAclient.exe" : "mega-exec");
    public string ServerExecutable => GetExecutableWithValidation(OperatingSystem.IsWindows() ? "MEGAcmdServer.exe" :
        OperatingSystem.IsMacOS() ? "mega-cmd" : MegaCmdRelease.ServerFileName);
    public string LibraryDirectory => Path.Combine(Path.GetDirectoryName(Executable)!, "lib");

    public override Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct) =>
        Task.FromResult(new DependentComponentVersion { Version = OperatingSystem.IsLinux() ? MegaCmdRelease.Version : "2.7.0",
            Description = "Reviewed official MEGAcmd packages. Linux managed installation requires glibc 2.35+, libstdc++ 12+ and libfuse.so.2. Windows/macOS use the official installer." });

    protected override async Task InstallCore(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Install MEGAcmd using the official operating-system installer, then rediscover it.");
        var asset = MegaCmdRelease.Select(DependentComponentPlatforms.CurrentArchitecture);
        var stage = Path.Combine(TempDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        var retainStage = false;
        try
        {
            using var client = clients.CreateClient(InternalOptions.HttpClientNames.Default);
            client.Timeout = TimeSpan.FromMinutes(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Bakabase/1.0");
            var archive = Path.Combine(stage, "release.deb");
            using (var response = await client.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > 100 * 1024 * 1024)
                    throw new InvalidDataException("MEGAcmd release is unexpectedly large.");
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = new FileStream(archive, FileMode.CreateNew);
                var buffer = new byte[81920]; long count = 0; int read;
                while ((read = await input.ReadAsync(buffer, ct)) != 0)
                {
                    count += read;
                    if (count > 100 * 1024 * 1024) throw new InvalidDataException("MEGAcmd release is unexpectedly large.");
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }
            await UpdateContext(c => c.InstallationProgress = 75);
            var verified = Path.Combine(stage, "verified");
            await MegaCmdRelease.ExtractVerifiedAsync(archive, verified, asset, ct);
            ct.ThrowIfCancellationRequested();
            var destination = Path.Combine(DefaultLocation, asset.DirectoryName);
            if (await MegaCmdRelease.VerifyInstallationAsync(destination, asset, ct)) return;
            var previous = Path.Combine(stage, "previous");
            if (Directory.Exists(destination)) Directory.Move(destination, previous);
            try { Directory.Move(verified, destination); }
            catch
            {
                if (Directory.Exists(previous))
                {
                    try { Directory.Move(previous, destination); }
                    catch { retainStage = true; throw; }
                }
                throw;
            }
        }
        finally { if (!retainStage && Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
}
