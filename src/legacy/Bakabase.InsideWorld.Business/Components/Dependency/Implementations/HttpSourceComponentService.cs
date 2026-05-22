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
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Storage;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;
using Semver;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations
{
    public abstract class HttpSourceComponentService(ILoggerFactory loggerFactory, AppService appService,
            string directoryName,
            IHttpClientFactory httpClientFactory, IServiceProvider globalServiceProvider)
        : DependentComponentService(loggerFactory, appService, directoryName, globalServiceProvider)
    {
        protected HttpClient HttpClient = httpClientFactory.CreateClient(InternalOptions.HttpClientNames.Default);
        private readonly ILoggerFactory _loggerFactory = loggerFactory;

        protected abstract Task<Dictionary<string, string>> GetDownloadUrls(DependentComponentVersion version,
            CancellationToken ct);

        protected abstract Task PostDownloading(List<string> files, CancellationToken ct);
        private const int InstallationProgressForDownloading = 90;

        protected override async Task InstallCore(CancellationToken ct)
        {
            await Discover(ct);
            var latestVersion = await GetLatestVersion(ct);
            Logger.LogInformation($"Try to install latest version: {latestVersion.Version}");

            var currentVer = TryParseComponentVersion(Context.Version);
            var latestVer = TryParseComponentVersion(latestVersion.Version);
            if (latestVer == null)
            {
                Logger.LogWarning(
                    $"Unable to parse latest version [{latestVersion.Version}] of {DisplayName}, skipping installation.");
                return;
            }

            if (currentVer == null || latestVer.ComparePrecedenceTo(currentVer) > 0)
            {
                var urlAndFileNames = await GetDownloadUrls(latestVersion, ct);
                if (urlAndFileNames.Any())
                {
                    Directory.CreateDirectory(TempDirectory);
                    var perFileProgress = (decimal) InstallationProgressForDownloading / urlAndFileNames.Count;
                    var singleFileDownloader = new SingleFileHttpDownloader(HttpClient,
                        _loggerFactory.CreateLogger<SingleFileHttpDownloader>());
                    var allFilePaths = new List<string>();
                    singleFileDownloader.OnProgress += async (progress) =>
                    {
                        var newProgress = (int) (perFileProgress * allFilePaths.Count +
                                                 progress * perFileProgress / 100);
                        if (newProgress != Context.InstallationProgress)
                        {
                            await UpdateContext(d =>
                            {
                                d.InstallationProgress = newProgress;
                            });
                        }
                    };
                    foreach (var (url, fileName) in urlAndFileNames)
                    {
                        var filePath = Path.Combine(TempDirectory, fileName);
                        var dir = Path.GetDirectoryName(filePath)!;
                        Directory.CreateDirectory(dir);
                        await singleFileDownloader.Download(url, filePath, ct);
                        allFilePaths.Add(filePath);
                    }

                    await PostDownloading(allFilePaths, ct);
                }

                DirectoryUtils.Delete(TempDirectory, true, false);
            }
        }

        /// <summary>
        /// Parses a component version string tolerantly. Some components report
        /// 4-segment versions (e.g. Locale Emulator's "2.5.0.1") that SemVer
        /// cannot parse; those are truncated to the first three segments.
        /// Returns null when the value still cannot be parsed.
        /// </summary>
        private static SemVersion? TryParseComponentVersion(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return null;
            }

            if (SemVersion.TryParse(version, SemVersionStyles.Any, out var semVersion))
            {
                return semVersion;
            }

            var segments = version.Split('.');
            if (segments.Length > 3)
            {
                var truncated = string.Join('.', segments.Take(3));
                if (SemVersion.TryParse(truncated, SemVersionStyles.Any, out semVersion))
                {
                    return semVersion;
                }
            }

            return null;
        }
    }
}