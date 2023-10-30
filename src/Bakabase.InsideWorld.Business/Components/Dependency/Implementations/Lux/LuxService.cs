using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Lux.Models;
using Bootstrap.Components.Storage;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Lux
{
    public class LuxService
    (ILoggerFactory loggerFactory, AppService appService, IHttpClientFactory httpClientFactory,
        CompressedFileService compressedFileService) :
        HttpSourceComponentService(loggerFactory, appService, "lux", httpClientFactory)
    {
        public override string Id => "6cb4ac7f-f60d-45c9-86bc-a48918623654";
        public override string DisplayName => "lux";

        private const string LatestReleaseApiUrl = "https://api.github.com/repos/iawia002/lux/releases/latest";

        public override async Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct)
        {
            var json = await HttpClient.GetStringAsync(LatestReleaseApiUrl, ct);
            var release = JsonConvert.DeserializeObject<GithubRelease>(json)!;
            var version = release.Name;

            var targetOs = AppService.OsPlatform switch
            {
                OsPlatform.Windows => "Windows",
                OsPlatform.Osx => "Linux",
                OsPlatform.Linux => "Linux",
                OsPlatform.FreeBsd => "Freebsd",
                _ => throw new ArgumentOutOfRangeException($"Current OS [{AppService.OsPlatform}] is not supported.")
            };

            var currentArch = RuntimeInformation.OSArchitecture;
            var targetArch = currentArch switch
            {
                Architecture.X86 => "i386",
                Architecture.X64 => "x86_64",
                Architecture.Arm64 => "arm64",
                _ => throw new ArgumentOutOfRangeException($"Current architecture [{currentArch}] is not supported.")
            };

            var targetAsset = release.Assets.FirstOrDefault(asset =>
            {
                var name = asset.Name;
                var segments = name.Split('-');
                var lastSegment = segments.Last();
                var dotIndexInLastSegment = lastSegment.IndexOf('.');
                if (dotIndexInLastSegment > -1)
                {
                    lastSegment = lastSegment.Substring(0, dotIndexInLastSegment);
                }

                var runtimeIdentifierSegments = segments.Skip(2).Take(segments.Length - 3).ToList();
                runtimeIdentifierSegments.Add(lastSegment);

                var os = runtimeIdentifierSegments[0];
                var arch = string.Join('_', runtimeIdentifierSegments.Skip(1));

                return targetOs == os && targetArch == arch;
            });

            if (targetAsset == null)
            {
                throw new Exception(
                    $"Can not find a target build for current runtime: {RuntimeInformation.RuntimeIdentifier}, check https://github.com/iawia002/lux/releases/latest for more information.");
            }

            return new LuxVersion()
            {
                CanUpdate = Context.Version != version,
                Description = release.Body,
                Version = version,
                DownloadUrl = targetAsset.BrowserDownloadUrl
            };
        }

        protected override IDiscoverer Discoverer { get; } = new LuxDiscover(loggerFactory);

        protected override async Task<List<string>> GetDownloadUrls(DependentComponentVersion version,
            CancellationToken ct)
        {
            return new List<string> {(version as LuxVersion)!.DownloadUrl};
        }

        protected override async Task PostDownloading(List<string> files, CancellationToken ct)
        {
            foreach (var fullFilename in files)
            {
                await compressedFileService.ExtractToCurrentDirectory(fullFilename, true, ct);
            }

            var everything = Directory.GetFileSystemEntries(TempDirectory);
            var downloadedFileNames = files.Select(Path.GetFileName).ToHashSet();

            foreach (var e in everything)
            {
                if (!downloadedFileNames.Contains(Path.GetFileName(e)))
                {
                    await DirectoryUtils.MoveAsync(e, DefaultLocation, true, null, ct);
                }
            }
        }
    }
}