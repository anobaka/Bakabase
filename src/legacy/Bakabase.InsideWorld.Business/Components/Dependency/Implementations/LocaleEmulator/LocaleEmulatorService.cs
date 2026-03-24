using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.LocaleEmulator.Models;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.SevenZip;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.SevenZip.Models;
using Bootstrap.Components.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.LocaleEmulator
{
    /// <summary>
    /// Manages the Locale Emulator component, which allows running applications
    /// with a different system locale (e.g., Japanese locale for DLsite games).
    /// Windows-only component.
    /// </summary>
    public class LocaleEmulatorService(
        ILoggerFactory loggerFactory,
        AppService appService,
        IHttpClientFactory httpClientFactory,
        IServiceProvider globalServiceProvider) : HttpSourceComponentService(loggerFactory,
        appService, "locale-emulator", httpClientFactory, globalServiceProvider)
    {
        public override string Id => "locale-emulator-component-service";
        protected override string KeyInLocalizer => "LocaleEmulator";
        public override bool IsRequired => false;

        protected override IDiscoverer Discoverer { get; } = new LocaleEmulatorDiscoverer(loggerFactory);

        protected override IEnumerable<Type> Dependencies => [typeof(SevenZipService)];

        private const string LatestReleaseApiUrl =
            "https://api.github.com/repos/xupefei/Locale-Emulator/releases/latest";

        /// <summary>
        /// Path to LEProc.exe, used for launching applications with locale emulation.
        /// </summary>
        public string LEProcExecutable => GetExecutableWithValidation("LEProc");

        public override bool IsAvailableOnCurrentPlatform => AppService.OsPlatform == OsPlatform.Windows;

        public override async Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct)
        {
            if (!IsAvailableOnCurrentPlatform)
            {
                return new LocaleEmulatorVersion
                {
                    Version = "N/A",
                    Description = "Locale Emulator is only available on Windows",
                    CanUpdate = false
                };
            }

            var json = await HttpClient.GetStringAsync(LatestReleaseApiUrl, ct);
            var release = JsonConvert.DeserializeObject<GithubRelease>(json)!;

            var version = release.TagName.TrimStart('v');

            // Find the .zip asset (LE distributes as a zip file)
            var targetAsset = release.Assets.FirstOrDefault(a =>
                a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

            if (targetAsset == null)
            {
                throw new Exception(
                    $"Cannot find a suitable Locale Emulator download. " +
                    $"Available assets: {string.Join(", ", release.Assets.Select(a => a.Name))}");
            }

            return new LocaleEmulatorVersion
            {
                Description = release.Body,
                Version = version,
                DownloadUrl = targetAsset.BrowserDownloadUrl,
                CanUpdate = string.IsNullOrEmpty(Context.Version) || Context.Version != version
            };
        }

        protected override async Task<Dictionary<string, string>> GetDownloadUrls(DependentComponentVersion version,
            CancellationToken ct)
        {
            var leVersion = (version as LocaleEmulatorVersion)!;
            return new Dictionary<string, string>
            {
                { leVersion.DownloadUrl, Path.GetFileName(leVersion.DownloadUrl)! }
            };
        }

        protected override async Task PostDownloading(List<string> filePaths, CancellationToken ct)
        {
            var sevenZipService = globalServiceProvider.GetRequiredService<SevenZipService>();

            foreach (var filePath in filePaths)
            {
                if (filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    await sevenZipService.Extract(filePath, TempDirectory, ct);
                    FileUtils.Delete(filePath, true, true);
                }
            }

            await DirectoryUtils.MoveAsync(TempDirectory, DefaultLocation, true, null,
                Bootstrap.Components.Tasks.PauseToken.None, ct);
        }

        public override async Task Install(CancellationToken ct)
        {
            if (!IsAvailableOnCurrentPlatform)
            {
                Logger.LogWarning("Locale Emulator is only available on Windows. Skipping installation.");
                return;
            }

            await base.Install(ct);
        }

        /// <summary>
        /// Launches an executable with Japanese locale emulation.
        /// </summary>
        /// <param name="exePath">Path to the executable to launch</param>
        /// <param name="ct">Cancellation token</param>
        public async Task LaunchWithLocaleEmulator(string exePath, CancellationToken ct)
        {
            if (!IsAvailableOnCurrentPlatform)
            {
                throw new PlatformNotSupportedException("Locale Emulator is only available on Windows");
            }

            var output = new System.Text.StringBuilder();
            var error = new System.Text.StringBuilder();

            // LEProc.exe -run <executable>
            var cmd = CliWrap.Cli.Wrap(LEProcExecutable)
                .WithArguments(["-run", exePath])
                .WithValidation(CliWrap.CommandResultValidation.None)
                .WithStandardOutputPipe(CliWrap.PipeTarget.ToStringBuilder(output))
                .WithStandardErrorPipe(CliWrap.PipeTarget.ToStringBuilder(error));

            var result = await cmd.ExecuteAsync(ct);

            if (result.ExitCode != 0)
            {
                Logger.LogError(
                    $"Failed to launch {exePath} with Locale Emulator. Exit code: {result.ExitCode}, Error: {error}");
                throw new Exception(
                    $"Failed to launch with Locale Emulator: {error}");
            }

            Logger.LogInformation($"Successfully launched {exePath} with Locale Emulator");
        }
    }
}
