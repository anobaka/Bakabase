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
using Microsoft.Extensions.Logging;
using Semver;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations
{
    public abstract class HttpSourceComponentService(ILoggerFactory loggerFactory, AppService appService, string directoryName,
            IHttpClientFactory httpClientFactory)
        : DependentComponentService(loggerFactory, appService, directoryName)
    {
        protected HttpClient HttpClient = httpClientFactory.CreateClient(BusinessConstants.HttpClientNames.Default);

        protected abstract Task<List<string>> GetDownloadUrls(DependentComponentVersion version,
            CancellationToken ct);

        protected abstract Task PostDownloading(List<string> files, CancellationToken ct);
        private const int InstallationProgressForDownloading = 90;

        protected override async Task InstallCore(CancellationToken ct)
        { 
            await Discover(ct);
            var latestVersion = await GetLatestVersion(ct);
            Logger.LogInformation($"Try to install latest version: {latestVersion.Version}");

            if (Context.Version == null || SemVersion.Parse(latestVersion.Version, SemVersionStyles.Any)
                    .ComparePrecedenceTo(SemVersion.Parse(Context.Version, SemVersionStyles.Any)) > 0)
            {
                var urls = await GetDownloadUrls(latestVersion, ct);
                if (urls.Any())
                {
                    Directory.CreateDirectory(TempDirectory);
                    var perFileProgress = (decimal)InstallationProgressForDownloading / urls.Count;
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
                    foreach (var url in urls)
                    {
                        var filename = Path.GetFileName(url);
                        var filePath = Path.Combine(TempDirectory, filename);
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