using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Models;
using Microsoft.Extensions.DependencyInjection;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Storage;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations
{
    public abstract class HttpSourceComponentService(ILoggerFactory loggerFactory, AppService appService,
            string directoryName,
            IHttpClientFactory httpClientFactory, IServiceProvider globalServiceProvider)
        : DependentComponentService(loggerFactory, appService, directoryName, globalServiceProvider)
    {
        protected HttpClient HttpClient = httpClientFactory.CreateClient(InternalOptions.HttpClientNames.Default);
        private readonly IHttpDownloader _downloader = globalServiceProvider.GetRequiredService<IHttpDownloader>();

        protected abstract Task<Dictionary<string, string>> GetDownloadUrls(DependentComponentVersion version,
            CancellationToken ct);

        protected abstract Task PostDownloading(List<string> files, CancellationToken ct);
        private const int InstallationProgressForDownloading = 90;

        protected override async Task InstallCore(CancellationToken ct)
        {
            // The base class discovers under its operation lock before entering installation.
            var latestVersion = await GetLatestVersion(ct);
            Logger.LogInformation($"Try to install latest version: {latestVersion.Version}");

            if (ComponentVersionComparison.TryParse(latestVersion.Version) == null)
            {
                OnLatestVersionNotInstallable(latestVersion);
                Logger.LogWarning(
                    $"Unable to parse latest version [{latestVersion.Version}] of {DisplayName}, skipping installation.");
                return;
            }

            if (ShouldDownload(Context.Version, latestVersion.Version))
            {
                var urlAndFileNames = await GetDownloadUrls(latestVersion, ct);
                if (urlAndFileNames.Any())
                {
                    Directory.CreateDirectory(TempDirectory);
                    var perFileProgress = (decimal) InstallationProgressForDownloading / urlAndFileNames.Count;
                    var allFilePaths = new List<string>();
                    async Task ReportProgress(int progress, string? message)
                    {
                        var newProgress = (int) (perFileProgress * allFilePaths.Count +
                                                 progress * perFileProgress / 100);
                        if (newProgress != Context.InstallationProgress)
                        {
                            await UpdateContext(d => d.InstallationProgress = newProgress);
                        }
                    }
                    foreach (var (url, fileName) in urlAndFileNames)
                    {
                        var filePath = Path.Combine(TempDirectory, fileName);
                        var dir = Path.GetDirectoryName(filePath)!;
                        Directory.CreateDirectory(dir);
                        await _downloader.DownloadAsync(new HttpDownloadRequest(url, dir)
                        {
                            FileName = Path.GetFileName(filePath),
                            HttpClientName = InternalOptions.HttpClientNames.Default
                        }, ReportProgress, ct);
                        allFilePaths.Add(filePath);
                    }

                    await PostDownloading(allFilePaths, ct);
                }

                DirectoryUtils.Delete(TempDirectory, true, false);
            }
        }

        /// <summary>
        /// The same decision the update prompt shows (<see cref="ComponentVersionComparison.IsUpdateAvailable"/>),
        /// except that an explicit install over an installed copy whose version cannot be parsed
        /// still proceeds: the prompt does not nag about such a copy, but the user asked.
        /// </summary>
        internal static bool ShouldDownload(string? installedVersion, string? latestVersion) =>
            ComponentVersionComparison.TryParse(installedVersion) == null ||
            ComponentVersionComparison.IsUpdateAvailable(installedVersion, latestVersion);

        /// <summary>
        /// Called when the latest version carries no installable version (e.g. "N/A" for a runtime
        /// the source publishes no build for), before the installation is skipped. Throw here to
        /// tell an explicit install why nothing can be installed.
        /// </summary>
        protected virtual void OnLatestVersionNotInstallable(DependentComponentVersion latestVersion)
        {
        }
    }
}
