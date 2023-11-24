using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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
            IHttpClientFactory httpClientFactory)
        : DependentComponentService(loggerFactory, appService, directoryName)
    {
        protected HttpClient HttpClient = httpClientFactory.CreateClient(BusinessConstants.HttpClientNames.Default);

        protected abstract Task<Dictionary<string, string>> GetDownloadUrls(DependentComponentVersion version,
            CancellationToken ct);

        protected abstract Task PostDownloading(List<string> files, CancellationToken ct);
        private const int InstallationProgressForDownloading = 90;

        protected override async Task InstallCore(CancellationToken ct)
        {
            await Discover(ct);
            var latestVersion = await GetLatestVersion(ct);
            Logger.LogInformation($"Try to install latest version: {latestVersion.Version}");

            SemVersion? currentVer = null;
            if (Context.Version.IsNotEmpty())
            {
                try
                {
                    currentVer = SemVersion.Parse(Context.Version, SemVersionStyles.Any);
                }
                catch (Exception e)
                {
                    Logger.LogError(e, $"An error occurred during parsing current version [{Context.Version}]: {e.Message}");
                }
            }

            if (currentVer == null || SemVersion.Parse(latestVersion.Version, SemVersionStyles.Any).ComparePrecedenceTo(currentVer) > 0)
            {
                var urlAndFileNames = await GetDownloadUrls(latestVersion, ct);
                if (urlAndFileNames.Any())
                {
                    Directory.CreateDirectory(TempDirectory);
                    var perFileProgress = (decimal) InstallationProgressForDownloading / urlAndFileNames.Count;
                    var singleFileDownloader = new SingleFileHttpDownloader(HttpClient,
                        loggerFactory.CreateLogger<SingleFileHttpDownloader>());
                    var allFilePaths = new List<string>();
                    singleFileDownloader.OnProgress += async (progress) =>
                    {
                        if (progress != Context.InstallationProgress)
                        {
                            await UpdateContext(d =>
                            {
                                d.InstallationProgress = (int) (perFileProgress * allFilePaths.Count +
                                                                progress * perFileProgress / 100);
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
    }
}