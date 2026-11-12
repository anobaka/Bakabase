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

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Rclone;

public sealed class RcloneService(ILoggerFactory loggerFactory, AppService appService,
    IHttpClientFactory clients, IServiceProvider services)
    : DependentComponentService(loggerFactory, appService, "rclone", services)
{
    public override string Id => "rclone-component-service";
    protected override string KeyInLocalizer => "Rclone";
    public override bool IsRequired => false;
    public override IReadOnlyList<DependentComponentTarget> SupportedTargets => DependentComponentPlatforms.Rclone;
    protected override IDiscoverer Discoverer { get; } = new RcloneDiscoverer(loggerFactory);
    public string Executable => GetExecutableWithValidation(OperatingSystem.IsWindows() ? "rclone.exe" : "rclone");

    // The installer never silently adopts an unreviewed upstream update. Updating this version
    // requires reviewing the CLI/backend changes and replacing all six pinned release digests.
    public override Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct) =>
        Task.FromResult(new DependentComponentVersion { Version = RcloneRelease.Version,
            Description = "Reviewed rclone v1.75.2 official release; MIT license (included in README.txt)." });

    protected override async Task InstallCore(CancellationToken ct)
    {
        var asset = RcloneRelease.Select(DependentComponentPlatforms.CurrentOs, DependentComponentPlatforms.CurrentArchitecture);
        var stage = Path.Combine(TempDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            using var client = clients.CreateClient(InternalOptions.HttpClientNames.Default);
            client.Timeout = TimeSpan.FromMinutes(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Bakabase/1.0");
            var archive = Path.Combine(stage, "release.zip");
            using (var response = await client.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > 100 * 1024 * 1024)
                    throw new InvalidDataException("Rclone release is unexpectedly large.");
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = new FileStream(archive, FileMode.CreateNew);
                var buffer = new byte[81920]; long count = 0; int read;
                while ((read = await input.ReadAsync(buffer, ct)) != 0)
                {
                    count += read;
                    if (count > 100 * 1024 * 1024) throw new InvalidDataException("Rclone release is unexpectedly large.");
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }
            await UpdateContext(c => c.InstallationProgress = 70);
            var extracted = Path.Combine(stage, "verified");
            await RcloneRelease.ExtractVerifiedAsync(archive, extracted, asset, ct);
            var found = await ((RcloneDiscoverer)Discoverer).TryDiscoverAt(extracted, ct);
            if (found?.Version != RcloneRelease.Version) throw new InvalidDataException("Rclone release validation failed.");
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(DefaultLocation);
            // Each file replacement is atomic. A cancelled download/extraction never touches the
            // working installation, and the executable is published only after validation.
            File.Move(Path.Combine(extracted, "README.txt"), Path.Combine(DefaultLocation, "README.txt"), true);
            File.Move(Path.Combine(extracted, Path.GetFileName(ExecutableName)), Path.Combine(DefaultLocation, ExecutableName), true);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }

    private static string ExecutableName => OperatingSystem.IsWindows() ? "rclone.exe" : "rclone";
}
