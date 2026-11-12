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

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.TeraBoxCli;

public sealed class TeraBoxCliService(ILoggerFactory loggerFactory, AppService appService,
    IHttpClientFactory clients, IServiceProvider services)
    : DependentComponentService(loggerFactory, appService, "terabox-cli", services)
{
    public override string Id => "terabox-cli-component-service";
    protected override string KeyInLocalizer => "TeraBoxCli";
    public override bool IsRequired => false;
    public override IReadOnlyList<DependentComponentTarget> SupportedTargets => DependentComponentPlatforms.TeraBoxCli;
    protected override IDiscoverer Discoverer { get; } = new TeraBoxCliDiscoverer(loggerFactory);
    private static string ExecutableName => OperatingSystem.IsWindows() ? "terabox.exe" : "terabox";
    public string Executable => GetExecutableWithValidation(ExecutableName);
    public override Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct) =>
        Task.FromResult(new DependentComponentVersion { Version = TeraBoxCliRelease.Version,
            Description = "Reviewed TeraBox CLI v1.1.0 from the vendor CDN. Distributed by TeraBox; subject to its service terms." });

    protected override async Task InstallCore(CancellationToken ct)
    {
        var asset = TeraBoxCliRelease.Select(DependentComponentPlatforms.CurrentOs, DependentComponentPlatforms.CurrentArchitecture);
        var stage = Path.Combine(TempDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            using var client = clients.CreateClient(InternalOptions.HttpClientNames.Default);
            client.Timeout = TimeSpan.FromMinutes(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Bakabase/1.0");
            var archive = Path.Combine(stage, asset.Name);
            using (var response = await client.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > TeraBoxCliRelease.MaximumArchiveBytes)
                    throw new InvalidDataException("TeraBox CLI release is unexpectedly large.");
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = new FileStream(archive, FileMode.CreateNew);
                var buffer = new byte[81920]; long count = 0; int read;
                while ((read = await input.ReadAsync(buffer, ct)) != 0)
                {
                    count += read;
                    if (count > TeraBoxCliRelease.MaximumArchiveBytes) throw new InvalidDataException("TeraBox CLI release is unexpectedly large.");
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }
            await UpdateContext(c => c.InstallationProgress = 80);
            var extracted = Path.Combine(stage, "verified");
            await TeraBoxCliRelease.ExtractVerifiedAsync(archive, extracted, asset, ct);
            var binary = Path.Combine(extracted, ExecutableName);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(binary,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var found = await ((TeraBoxCliDiscoverer)Discoverer).TryDiscoverAt(extracted, ct);
            if (found?.Version != TeraBoxCliRelease.Version) throw new InvalidDataException("TeraBox CLI release validation failed.");
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(DefaultLocation);
            File.Move(binary, Path.Combine(DefaultLocation, ExecutableName), true);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
}
