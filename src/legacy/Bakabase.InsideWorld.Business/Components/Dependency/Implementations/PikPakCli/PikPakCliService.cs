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

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.PikPakCli;

public sealed class PikPakCliService(ILoggerFactory loggerFactory, AppService appService,
    IHttpClientFactory clients, IServiceProvider services)
    : DependentComponentService(loggerFactory, appService, "pikpak-cli", services)
{
    public override string Id => "pikpak-cli-component-service";
    protected override string KeyInLocalizer => "PikPakCli";
    public override bool IsRequired => false;
    public override IReadOnlyList<DependentComponentTarget> SupportedTargets => DependentComponentPlatforms.PikPakCli;
    protected override IDiscoverer Discoverer { get; } = new PikPakCliDiscoverer(loggerFactory);
    private static string ExecutableName => OperatingSystem.IsWindows() ? "pikpak.exe" : "pikpak";
    public string Executable => GetExecutableWithValidation(ExecutableName);
    public override Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct) =>
        Task.FromResult(new DependentComponentVersion { Version = PikPakCliRelease.Version,
            Description = "Reviewed official PikPak CLI v0.6.0. Distributed by PikPak; subject to PikPak's terms of service." });

    protected override async Task InstallCore(CancellationToken ct)
    {
        var asset = PikPakCliRelease.Select(DependentComponentPlatforms.CurrentOs, DependentComponentPlatforms.CurrentArchitecture);
        var stage = Path.Combine(TempDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            using var client = clients.CreateClient(InternalOptions.HttpClientNames.Default);
            client.Timeout = TimeSpan.FromMinutes(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Bakabase/1.0");
            var binary = Path.Combine(stage, ExecutableName);
            using (var response = await client.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > 100 * 1024 * 1024)
                    throw new InvalidDataException("PikPak CLI release is unexpectedly large.");
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = new FileStream(binary, FileMode.CreateNew);
                var buffer = new byte[81920]; long count = 0; int read;
                while ((read = await input.ReadAsync(buffer, ct)) != 0)
                {
                    count += read;
                    if (count > 100 * 1024 * 1024) throw new InvalidDataException("PikPak CLI release is unexpectedly large.");
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }
            await UpdateContext(c => c.InstallationProgress = 80);
            await PikPakCliRelease.VerifyAsync(binary, asset, ct);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(binary,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var found = await ((PikPakCliDiscoverer)Discoverer).TryDiscoverAt(stage, ct);
            if (found?.Version != PikPakCliRelease.Version) throw new InvalidDataException("PikPak CLI release validation failed.");
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(DefaultLocation);
            File.Move(binary, Path.Combine(DefaultLocation, ExecutableName), true);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
}
