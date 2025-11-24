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
            foreach (var fullFilename in filePaths)
            {
                if (fullFilename.EndsWith(".tar.xz"))
                {
                    // Linux/Mac tarball - extract using system tar command
                    await ExtractUsingSystemTar(fullFilename, ct);
                    FileUtils.Delete(fullFilename, true, true);
                }
                else if (fullFilename.EndsWith(".exe"))
                {
                    // Windows .exe files are standalone executables, no extraction needed
                    // Just keep the file as-is
                }
            }

            await DirectoryUtils.MoveAsync(TempDirectory, DefaultLocation, true, null, PauseToken.None, ct);
        }

        private async Task ExtractUsingSystemTar(string archivePath, CancellationToken ct)
        {
            var outputDir = Path.GetDirectoryName(archivePath)!;
            var output = new System.Text.StringBuilder();
            var error = new System.Text.StringBuilder();

            // tar -xf archive.tar.xz -C outputDir
            var cmd = CliWrap.Cli.Wrap("tar")
                .WithArguments(new[] { "-xf", archivePath, "-C", outputDir })
                .WithValidation(CliWrap.CommandResultValidation.None)
                .WithStandardOutputPipe(CliWrap.PipeTarget.ToStringBuilder(output))
                .WithStandardErrorPipe(CliWrap.PipeTarget.ToStringBuilder(error));

            var result = await cmd.ExecuteAsync(ct);

            if (result.ExitCode != 0)
            {
                Logger.LogError($"Failed to extract {archivePath} using system tar. Exit code: {result.ExitCode}, Error: {error}");
                throw new Exception($"Failed to extract {archivePath} using system tar: {error}");
            }

            Logger.LogInformation($"Successfully extracted {archivePath} using system tar");
        }

        public override async Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct)
        {
            var json = await HttpClient.GetStringAsync(LatestReleaseApiUrl, ct);
            var release = JsonConvert.DeserializeObject<GithubRelease>(json)!;

            // Extract version from tag name (e.g., "24.08" from "v24.08" or "24.08")
            var version = release.TagName.TrimStart('v');

            // Determine the target asset name pattern based on OS and architecture
            var (osPart, archPart, extension) = AppService.OsPlatform switch
            {
                OsPlatform.Windows => RuntimeInformation.OSArchitecture switch
                {
                    Architecture.X64 => ("x64", "", ".exe"),
                    Architecture.X86 => ("", "", ".exe"),
                    Architecture.Arm64 => ("arm64", "", ".exe"),
                    _ => throw new NotSupportedException(
                        $"Architecture {RuntimeInformation.OSArchitecture} is not supported on Windows")
                },
                OsPlatform.Osx => ("mac", "", ".tar.xz"),
                OsPlatform.Linux => RuntimeInformation.OSArchitecture switch
                {
                    Architecture.X64 => ("linux-x64", "", ".tar.xz"),
                    Architecture.Arm64 => ("linux-arm64", "", ".tar.xz"),
                    Architecture.Arm => ("linux-arm", "", ".tar.xz"),
                    _ => throw new NotSupportedException(
                        $"Architecture {RuntimeInformation.OSArchitecture} is not supported on Linux")
                },
                _ => throw new NotSupportedException($"OS Platform {AppService.OsPlatform} is not supported")
            };

            // Find the matching asset
            // Asset naming pattern examples:
            // - Windows x64: 7z2408-x64.exe
            // - Windows x86: 7z2408.exe
            // - Windows ARM64: 7z2408-arm64.exe
            // - Linux x64: 7z2408-linux-x64.tar.xz
            // - Mac: 7z2408-mac.tar.xz
            var targetAsset = release.Assets.FirstOrDefault(asset =>
            {
                var name = asset.Name.ToLower();

                // Check if it's a 7z release file and has the correct extension
                if (!name.StartsWith("7z") || !name.EndsWith(extension))
                {
                    return false;
                }

                // For Windows x86 (no arch suffix)
                if (AppService.OsPlatform == OsPlatform.Windows &&
                    RuntimeInformation.OSArchitecture == Architecture.X86)
                {
                    // Match pattern: 7z<version>.exe (no arch suffix)
                    return !name.Contains("-") && name.EndsWith(".exe");
                }

                // For other platforms, match the OS and arch parts
                if (!string.IsNullOrEmpty(osPart))
                {
                    return name.Contains(osPart.ToLower());
                }

                return true;
            });

            if (targetAsset == null)
            {
                throw new Exception(
                    $"Cannot find a suitable 7-Zip build for {AppService.OsPlatform} {RuntimeInformation.OSArchitecture}. " +
                    $"Available assets: {string.Join(", ", release.Assets.Select(a => a.Name))}");
            }

            return new SevenZipVersion
            {
                Description = release.Body,
                Version = version,
                DownloadUrl = targetAsset.BrowserDownloadUrl,
                CanUpdate = string.IsNullOrEmpty(Context.Version) || Context.Version != version
            };
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
        public async Task Extract(string archivePath, string destinationPath, CancellationToken ct)
        {
            Directory.CreateDirectory(destinationPath);

            var args = new[]
            {
                "x", // Extract with full paths
                archivePath,
                $"-o{destinationPath}", // Output directory
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
