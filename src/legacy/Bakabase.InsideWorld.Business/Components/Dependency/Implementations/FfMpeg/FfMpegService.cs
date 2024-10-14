using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg.Models;
using Bakabase.InsideWorld.Business.Resources;
using Bootstrap.Components.Storage;
using CliWrap;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg
{
    public class FfMpegService(ILoggerFactory loggerFactory, AppService appService,
        IHttpClientFactory httpClientFactory, InsideWorldLocalizer localizer,
        CompressedFileService compressedFileService) : HttpSourceComponentService(loggerFactory,
        appService, "ffmpeg", httpClientFactory)
    {
        public override string Id => "364e3884-4c6f-446f-b72c-1ec84e8da2c2";
        public override string DisplayName => "ffmpeg";
        public override string? Description => "https://www.ffmpeg.org/";
        public override bool IsRequired => false;

        protected override IDiscoverer Discoverer { get; } = new FfMpegDiscoverer(loggerFactory);

        private const string HttpApi = "https://ffbinaries.com/api/v1/version/latest";

        protected override async Task<Dictionary<string, string>> GetDownloadUrls(DependentComponentVersion version,
            CancellationToken ct)
        {
            var ffmpegVer = (version as FfMpegVersion)!;
            var urls = new[]
            {
                ffmpegVer.FfMpegUrl,
                ffmpegVer.FfProbeUrl,
                ffmpegVer.FfPlayUrl
            }.Where(a => !string.IsNullOrEmpty(a)).ToList();

            return urls!.ToDictionary(a => a!, a => Path.GetFileName(a)!);
        }

        protected override async Task PostDownloading(List<string> filePaths, CancellationToken ct)
        {
            foreach (var fullFilename in filePaths)
            {
                await compressedFileService.ExtractToCurrentDirectory(fullFilename, true, ct);
            }

            await DirectoryUtils.MoveAsync(TempDirectory, DefaultLocation, true, null, ct);
        }

        public override async Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct)
        {
            var json = await HttpClient.GetStringAsync(HttpApi, ct);
            var version = JsonConvert.DeserializeObject<VersionRsp>(json)!;

            var osPart = AppService.OsPlatform switch
            {
                OsPlatform.Windows => "windows",
                OsPlatform.Osx => "osx",
                OsPlatform.Linux => "linux",
                OsPlatform.FreeBsd => "linux",
                OsPlatform.Unknown => "unknown",
                _ => throw new ArgumentOutOfRangeException()
            };

            var archPart = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X86 => "32",
                Architecture.X64 => "64",
                Architecture.Arm => "arm",
                Architecture.Arm64 => "arm64",
                Architecture.Wasm => "wasm",
                Architecture.S390x => "s390x",
                _ => throw new ArgumentOutOfRangeException()
            };

            var key = $"{osPart}-{archPart}";
            var nameAndUrls = version.Bin.GetValueOrDefault(key);

            if (nameAndUrls == null)
            {
                var runtimeIdentifier = RuntimeInformation.RuntimeIdentifier;
                var message =
                    $"Runtime is not supported: {runtimeIdentifier}. Supported runtimes are: {string.Join(',', version.Bin.Keys)}";
                Logger.LogError(message);
                throw new NotSupportedException(message);
            }

            var fv = new FfMpegVersion
            {
                Description = null,
                Version = version.Version,
                FfMpegUrl = nameAndUrls.FfMpeg,
                FfProbeUrl = nameAndUrls.FfProbe,
                FfPlayUrl = nameAndUrls.FfPlay,
                CanUpdate = string.IsNullOrEmpty(Context.Version) || !Context.Version.Contains(version.Version)
            };

            return fv;
        }

        public string FfProbeExecutable => GetExecutableWithValidation("ffprobe");
        public string FfMpegExecutable => GetExecutableWithValidation("ffmpeg");

        public async Task<double> GetDuration(string path, CancellationToken ct)
        {
            // https://trac.ffmpeg.org/ticket/8890
            // Console.OutputEncoding = Encoding.UTF8;
            // ffprobe.exe simply writes utf8 data to stdout, and the Xabe.FFmpeg has no way to specify encoding.
            // var info = await FFmpeg.GetMediaInfo(firstVideoFile.FullName, ct);
            var output = new StringBuilder();
            var error = new StringBuilder();
            var cmd = Cli.Wrap(FfProbeExecutable)
                .WithArguments(new[]
                {
                    "-v", "quiet",
                    "-print_format", "json",
                    "-show_entries", "format=duration",
                    path
                }, true)
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.ToStringBuilder(output))
                .WithStandardErrorPipe(PipeTarget.ToStringBuilder(error));

            // var x2 = await FFmpeg.GetMediaInfo(firstVideoFile.FullName, ct);
            var rsp = await cmd.ExecuteAsync(ct);
            if (rsp.ExitCode != 0)
            {
                throw new Exception(error.ToString());
            }

            var jObject = JObject.Parse(output.ToString());
            var seconds = jObject["format"]!["duration"]!.Value<double>();
            return seconds;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="videoFilePath"></param>
        /// <param name="time"></param>
        /// <param name="ct"></param>
        /// <returns>Jpeg format</returns>
        /// <exception cref="Exception"></exception>
        public async Task<MemoryStream> CaptureFrame(string videoFilePath, TimeSpan time, CancellationToken ct)
        {
            // var c = (await FFmpeg.Conversions.FromSnippet.Snapshot(firstVideoFile.FullName,
            //     tmpFile,
            //     screenshotTime));
            // var r = await c.Start(ct);
            // ffmpeg -i input.mp4 -ss 00:00:05 -vframes 1 frame_out.jpg1.
            var output = new StringBuilder();
            var error = new StringBuilder();
            var image = new MemoryStream();
            var timeString = $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}";
            var cmd = Cli.Wrap(FfMpegExecutable)
                .WithArguments(new[]
                {
                    "-ss", timeString,
                    "-r", "1:1",
                    "-i", videoFilePath,
                    "-c:v", "mjpeg",
                    "-f", "image2pipe",
                    "-vframes:v", "1",
                    "-preset", "ultrafast",
                    "-"
                }, true)
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.ToStream(image))
                .WithStandardErrorPipe(PipeTarget.ToStringBuilder(error));
            var rsp = await cmd.ExecuteAsync(ct);
            if (rsp.ExitCode != 0)
            {
                throw new Exception(error.ToString());
            }

            image.Seek(0, SeekOrigin.Begin);
            return image;
        }
    }
}