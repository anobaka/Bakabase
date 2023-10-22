using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Business.Resources;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Storage;
using Bootstrap.Models.Constants;
using CsQuery.Engine.PseudoClassSelectors;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Semver;
using Xabe.FFmpeg.Downloader;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Installer.FfMpeg
{
    public class FfMpegInstaller : IComponentInstaller
    {
        public string Id => "364e3884-4c6f-446f-b72c-1ec84e8da2c2";
        public string DisplayName => "FFMpeg";

        private const string HttpApi = "https://ffbinaries.com/api/v1/version/latest";

        private readonly HttpClient _client;
        private readonly ILogger<FfMpegInstaller> _logger;
        private readonly AppService _appService;
        private readonly InsideWorldLocalizer _localizer;
        private readonly CompressedFileService _compressedFileService;
        private const int DownloadBlockSize = 1_000_000;

        public FfMpegInstaller(HttpClient client, ILogger<FfMpegInstaller> logger, AppService appService,
            InsideWorldLocalizer localizer, CompressedFileService compressedFileService)
        {
            _client = client;
            _logger = logger;
            _appService = appService;
            _localizer = localizer;
            _compressedFileService = compressedFileService;

            _installationDirectory = Path.Combine(_appService.AppDataDirectory, "components", "ffmpeg");
            _infoFilePath = Path.Combine(_installationDirectory, "i.json");
        }

        private readonly string _installationDirectory;
        private readonly string _infoFilePath;

        private static readonly string[] KeyExecutables = {"ffprobe.exe", "ffmpeg.exe"};

        public string FfProbeExecutable
        {
            get
            {
                var path = Path.Combine(_installationDirectory, "ffprobe.exe");
                if (File.Exists(path))
                {
                    return path;
                }

                throw new FileNotFoundException(_localizer.PathIsNotFound(path));
            }
        }

        public string[] CheckMissingFiles() =>
            KeyExecutables.Where(x => !File.Exists(Path.Combine(_installationDirectory, x))).ToArray();

        public async Task<LocalInstallation?> CheckInstallation()
        {
            if (CheckMissingFiles().Any())
            {
                return null;
            }

            var li = new LocalInstallation
            {
                Location = _installationDirectory,
                Version = AppConstants.InitialVersion
            };

            if (File.Exists(_infoFilePath))
            {
                var info = JsonConvert.DeserializeObject<InstallationInfoFileData>(
                    await File.ReadAllTextAsync(_infoFilePath))!;
                li.Version = info.Version;
            }

            return li;
        }

        public async Task Install(CancellationToken ct)
        {
            var installation = await CheckInstallation();
            var latestVersion = (await GetLatestVersion() as FfMpegVersion)!;

            _logger.LogInformation($"Try to install latest version: {latestVersion.Version}");
            if (installation == null || SemVersion.Parse(latestVersion.Version, SemVersionStyles.Any)
                    .ComparePrecedenceTo(SemVersion.Parse(installation.Version, SemVersionStyles.Any)) > 0)
            {
                // download
                var tmpDir = Path.Combine(_installationDirectory, "temp");
                var urls = new[]
                {
                    latestVersion.FfMpegUrl,
                    latestVersion.FfProbeUrl,
                    latestVersion.FfPlayUrl
                }.Where(a => !string.IsNullOrEmpty(a)).ToList();

                var downloadedFileNames = new HashSet<string>();
                foreach (var url in urls)
                {
                    var rsp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url), ct);
                    var remoteMd5Bytes = rsp.Content.Headers.ContentMD5;
                    if (remoteMd5Bytes == null)
                    {
                        throw new Exception($"Got empty Content-MD5 from {url}");
                    }

                    var remoteMd5 = Convert.ToHexString(remoteMd5Bytes);
                    var fileSize = rsp.Content.Headers.ContentLength!.Value;
                    var downloadUrl = rsp.RequestMessage!.RequestUri!.ToString();

                    var filename = Path.GetFileName(url)!;
                    var fullFilename = Path.Combine(tmpDir, filename);

                    // assume the server supports HTTP Range
                    var fs = File.Open(fullFilename, FileMode.Append, FileAccess.ReadWrite, FileShare.None);
                    if (fs.Length > fileSize)
                    {
                        await fs.DisposeAsync();
                        File.Delete(fullFilename);
                        throw new Exception(
                            $"Current file size: {fs.Length} is larger than expected: {fileSize} and will be deleted.");
                    }

                    if (fs.Length < fileSize)
                    {
                        var downloadedBytesCount = fs.Length;
                        for (var blockStart = downloadedBytesCount;
                             blockStart < fileSize;
                             blockStart += DownloadBlockSize)
                        {
                            var blockRsp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, downloadUrl)
                            {
                                Headers =
                                {
                                    {
                                        "Range",
                                        $"bytes={blockStart}-{Math.Min(fileSize, blockStart + DownloadBlockSize) - 1}"
                                    }
                                }
                            }, ct);
                            blockRsp.EnsureSuccessStatusCode();
                            await blockRsp.Content.CopyToAsync(fs, ct);
                        }
                    }

                    if (fs.Length != fileSize)
                    {
                        throw new Exception(
                            $"Current file size: {fs.Length} does not equal to expected: {fileSize}");
                    }

                    var localMd5Bytes = await MD5.Create().ComputeHashAsync(File.OpenRead(fullFilename), ct);
                    var localMd5 = Convert.ToHexString(localMd5Bytes);

                    await fs.DisposeAsync();

                    if (localMd5 != remoteMd5)
                    {
                        File.Delete(fullFilename);
                        throw new Exception(
                            $"Failed to check MD5 for downloaded file, got: {localMd5} but expected: {remoteMd5}");
                    }

                    downloadedFileNames.Add(filename);
                }

                foreach (var fullFilename in downloadedFileNames.Select(fn => Path.Combine(tmpDir, fn)))
                {
                    await _compressedFileService.ExtractToCurrentDirectory(fullFilename, true, ct);
                }

                var everything = Directory.GetFileSystemEntries(tmpDir);

                foreach (var e in everything)
                {
                    if (!downloadedFileNames.Contains(Path.GetFileName(e)))
                    {
                        await DirectoryUtils.MoveAsync(e, _installationDirectory, true, null, ct);
                    }
                }

                DirectoryUtils.Delete(tmpDir, true, false);

                await File.WriteAllTextAsync(_infoFilePath, JsonConvert.SerializeObject(new InstallationInfoFileData
                {
                    Version = latestVersion.Version
                }), ct);
            }
        }

        public async Task<SimpleVersion> GetLatestVersion()
        {
            var json = await _client.GetStringAsync(HttpApi);
            var version = JsonConvert.DeserializeObject<VersionRsp>(json)!;

            var runtimeIdentifier = RuntimeInformation.RuntimeIdentifier;
            _logger.LogInformation($"System runtime identifier: {runtimeIdentifier}");

            VersionRsp.NameAndUrls nameAndUrls;
            switch (runtimeIdentifier)
            {
                case DotNetRids.Win64:
                    nameAndUrls = version.Bin["windows-64"];
                    break;
                case DotNetRids.Linux64:
                    nameAndUrls = version.Bin["linux-64"];
                    break;
                case DotNetRids.LinuxArm64:
                    nameAndUrls = version.Bin["linux-arm64"];
                    break;
                case DotNetRids.Osx64:
                    nameAndUrls = version.Bin["osx-64"];
                    break;
                default:
                {
                    var message = $"Runtime is not supported: {runtimeIdentifier}";
                    _logger.LogError(message);
                    throw new NotSupportedException(message);
                }
            }

            var fv = new FfMpegVersion
            {
                Description = null,
                Version = version.Version,
                FfMpegUrl = nameAndUrls.FfMpeg,
                FfProbeUrl = nameAndUrls.FfProbe,
                FfPlayUrl = nameAndUrls.FfPlay
            };

            return fv;
        }

        record FfMpegVersion : SimpleVersion
        {
            public string FfMpegUrl { get; set; } = null!;
            public string FfProbeUrl { get; set; } = null!;
            public string? FfPlayUrl { get; set; }
        }

        class VersionRsp
        {
            public string Version { get; set; } = null!;
            public string Permalink { get; set; } = null!;
            public Dictionary<string, NameAndUrls> Bin { get; set; } = null!;

            public class NameAndUrls
            {
                [JsonProperty("ffmpeg")] public string FfMpeg { get; set; } = null!;
                [JsonProperty("ffprobe")] public string FfProbe { get; set; } = null!;
                [JsonProperty("ffplay")] public string? FfPlay { get; set; }
            }
        }
    }
}