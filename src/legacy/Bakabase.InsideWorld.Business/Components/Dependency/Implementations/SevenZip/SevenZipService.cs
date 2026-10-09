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
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.SevenZip.Models;
using Bootstrap.Components.Storage;
using Bootstrap.Components.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.SevenZip
{
    public class SevenZipService(
        ILoggerFactory loggerFactory,
        AppService appService,
        IHttpClientFactory httpClientFactory,
        IServiceProvider globalServiceProvider) : HttpSourceComponentService(loggerFactory,
        appService, "7z", httpClientFactory, globalServiceProvider)
    {
        public override string Id => "7z-archiver-component-service";
        protected override string KeyInLocalizer => "7z";
        public override bool IsRequired => false;

        protected override IDiscoverer Discoverer { get; } = new SevenZipDiscoverer(loggerFactory);

        private const string LatestReleaseApiUrl = "https://api.github.com/repos/ip7z/7zip/releases/latest";

        protected override async Task<Dictionary<string, string>> GetDownloadUrls(DependentComponentVersion version,
            CancellationToken ct)
        {
            var sevenZipVer = (version as SevenZipVersion)!;
            return new Dictionary<string, string>
            {
                { sevenZipVer.DownloadUrl, Path.GetFileName(sevenZipVer.DownloadUrl)! }
            };
        }

        protected override async Task PostDownloading(List<string> filePaths, CancellationToken ct)
        {
            if (AppService.OsPlatform != OsPlatform.Windows)
            {
                await SevenZipUnixInstaller.InstallAsync(filePaths.Single(), DefaultLocation, loggerFactory, ct);
                return;
            }

            foreach (var fullFilename in filePaths)
            {
                if (fullFilename.EndsWith(".exe"))
                {
                    // Windows .exe files are standalone executables, no extraction needed
                    // Just keep the file as-is
                }
            }

            await DirectoryUtils.MoveAsync(TempDirectory, DefaultLocation, true, null, PauseToken.None, ct);
        }

        public override async Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct)
        {
            var json = await HttpClient.GetStringAsync(LatestReleaseApiUrl, ct);
            var release = JsonConvert.DeserializeObject<GithubRelease>(json)!;

            // Extract version from tag name (e.g., "24.08" from "v24.08" or "24.08")
            var version = release.TagName.TrimStart('v');

            var targetAsset = SelectReleaseAsset(release, AppService.OsPlatform, RuntimeInformation.OSArchitecture);

            return new SevenZipVersion
            {
                Description = release.Body,
                Version = version,
                DownloadUrl = targetAsset.BrowserDownloadUrl
            };
        }

        internal static GithubAsset SelectReleaseAsset(GithubRelease release, OsPlatform platform, Architecture architecture)
        {
            var suffix = platform switch
            {
                OsPlatform.Windows => architecture switch
                {
                    Architecture.X64 => "-x64.exe",
                    Architecture.X86 => ".exe",
                    Architecture.Arm64 => "-arm64.exe",
                    _ => throw new NotSupportedException(
                        $"Architecture {architecture} is not supported on Windows")
                },
                OsPlatform.Osx => "-mac.tar.xz",
                OsPlatform.Linux => architecture switch
                {
                    Architecture.X64 => "-linux-x64.tar.xz",
                    Architecture.Arm64 => "-linux-arm64.tar.xz",
                    Architecture.Arm => "-linux-arm.tar.xz",
                    _ => throw new NotSupportedException(
                        $"Architecture {architecture} is not supported on Linux")
                },
                _ => throw new NotSupportedException($"OS Platform {platform} is not supported")
            };

            // Exact names avoid confusing ARM with ARM64, or the Windows x86 installer with 7zr.exe.
            var name = "7z" + release.TagName.TrimStart('v').Replace(".", "") + suffix;
            return release.Assets.FirstOrDefault(asset => asset.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                   ?? throw new NotSupportedException(
                       $"Cannot find a suitable 7-Zip build for {platform} {architecture}. " +
                       $"Available assets: {string.Join(", ", release.Assets.Select(a => a.Name))}");
        }

        // On macOS and Linux, the executable is named "7zz", on Windows it's "7z.exe"
        public string SevenZipExecutable => GetExecutableWithValidation(
            AppService.OsPlatform == OsPlatform.Windows ? "7z" : "7zz");

        /// <summary>
        /// Extract an archive to a destination directory
        /// </summary>
        /// <param name="archivePath">Path to the archive file</param>
        /// <param name="destinationPath">Destination directory</param>
        /// <param name="ct">Cancellation token</param>
        /// <param name="codePage">Optional code page for filename encoding (e.g., 932 for Shift-JIS)</param>
        public async Task Extract(string archivePath, string destinationPath, CancellationToken ct, int? codePage = null)
        {
            await EnsureReadyAsync(ct);
            Directory.CreateDirectory(destinationPath);

            var argsList = new List<string>
            {
                "x", // Extract with full paths
                archivePath,
                $"-o{destinationPath}", // Output directory
                "-y" // Assume Yes on all queries
            };

            if (codePage.HasValue)
            {
                argsList.Add($"-mcp={codePage.Value}");
            }

            var output = new System.Text.StringBuilder();
            var error = new System.Text.StringBuilder();

            var cmd = CliWrap.Cli.Wrap(SevenZipExecutable)
                .WithArguments(argsList, false)
                .WithValidation(CliWrap.CommandResultValidation.None)
                .WithStandardOutputPipe(CliWrap.PipeTarget.ToStringBuilder(output))
                .WithStandardErrorPipe(CliWrap.PipeTarget.ToStringBuilder(error));

            var result = await cmd.ExecuteAsync(ct);

            if (result.ExitCode != 0)
            {
                throw new Exception($"7z extraction failed: {error}");
            }
        }

        /// <summary>
        /// Create an archive from files or directory
        /// </summary>
        /// <param name="sourcePath">Source file or directory path</param>
        /// <param name="archivePath">Destination archive path</param>
        /// <param name="compressionLevel">Compression level (0-9, default 5)</param>
        /// <param name="ct">Cancellation token</param>
        public async Task Compress(string sourcePath, string archivePath, int compressionLevel = 5, CancellationToken ct = default)
        {
            if (compressionLevel < 0 || compressionLevel > 9)
            {
                throw new ArgumentOutOfRangeException(nameof(compressionLevel), "Compression level must be between 0 and 9");
            }

            await EnsureReadyAsync(ct);

            var args = new[]
            {
                "a", // Add to archive
                archivePath,
                sourcePath,
                $"-mx={compressionLevel}", // Compression level
                "-y" // Assume Yes on all queries
            };

            var output = new System.Text.StringBuilder();
            var error = new System.Text.StringBuilder();

            var cmd = CliWrap.Cli.Wrap(SevenZipExecutable)
                .WithArguments(args, false)
                .WithValidation(CliWrap.CommandResultValidation.None)
                .WithStandardOutputPipe(CliWrap.PipeTarget.ToStringBuilder(output))
                .WithStandardErrorPipe(CliWrap.PipeTarget.ToStringBuilder(error));

            var result = await cmd.ExecuteAsync(ct);

            if (result.ExitCode != 0)
            {
                throw new Exception($"7z compression failed: {error}");
            }
        }
    }
}
