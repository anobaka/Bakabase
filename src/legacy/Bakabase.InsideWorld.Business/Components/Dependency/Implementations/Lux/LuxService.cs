using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Aliyun.OSS;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Lux.Models;
using Bootstrap.Components.Storage;
using Bootstrap.Components.Tasks;
using Bootstrap.Components.Terminal.Cmd;
using CliWrap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Lux
{
    public class LuxService(
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory,
        AppService appService,
        IHttpClientFactory httpClientFactory,
        CompressedFileService compressedFileService,
        IServiceProvider globalServiceProvider) :
        HttpSourceComponentService(loggerFactory, appService, "lux", httpClientFactory, globalServiceProvider)
    {
        public override string Id => "6cb4ac7f-f60d-45c9-86bc-a48918623654";

        public override bool IsRequired => false;
        protected override string KeyInLocalizer => "lux";
        private const string LatestReleaseApiUrl = "https://api.github.com/repos/iawia002/lux/releases/latest";
        protected FfMpegService FfMpegService => serviceProvider.GetRequiredService<FfMpegService>();

        protected string LuxBin => GetExecutableWithValidation("lux");

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
                var segments = name.Split('_');
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
                // version from cli has not leading 'v'
                CanUpdate = Context.Version != version.TrimStart('v'),
                Description = release.Body,
                Version = version,
                DownloadUrl = targetAsset.BrowserDownloadUrl
            };
        }

        protected override IDiscoverer Discoverer { get; } = new LuxDiscover(loggerFactory);

        protected override async Task<Dictionary<string, string>> GetDownloadUrls(DependentComponentVersion version,
            CancellationToken ct)
        {
            return new List<string> {(version as LuxVersion)!.DownloadUrl}.ToDictionary(a => a,
                a => Path.GetFileName(a)!);
        }

        protected override async Task PostDownloading(List<string> files, CancellationToken ct)
        {
            foreach (var fullFilename in files)
            {
                await compressedFileService.ExtractToCurrentDirectory(fullFilename, true, ct);
                FileUtils.Delete(fullFilename, true, true);
            }

            await DirectoryUtils.MoveAsync(TempDirectory, DefaultLocation, true, null, PauseToken.None, ct);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="url"></param>
        /// <param name="cookie"></param>
        /// <param name="downloadCaptions"></param>
        /// <param name="threadsCount"></param>
        /// <param name="outputDirectory"></param>
        /// <param name="outputName">Do not put file extension here.</param>
        /// <param name="onProgress"></param>
        /// <param name="otherArgs"></param>
        /// <returns></returns>
        public async Task<CmdResult> Download(string url, string? cookie, bool downloadCaptions, int threadsCount,
            string outputDirectory, string outputName, Func<int, Task>? onProgress, List<(string, object?)>? otherArgs)
        {
            var args = new List<(string Key, object? Value)>();
            if (!string.IsNullOrEmpty(cookie))
            {
                args.Add(("-c", cookie));
            }

            if (downloadCaptions)
            {
                args.Add(("-C", null));
            }

            if (threadsCount > 0)
            {
                args.Add(("-n", threadsCount));
            }

            args.Add(("-o", outputDirectory));
            args.Add(("-O", outputName));

            if (otherArgs?.Any() == true)
            {
                args.AddRange(otherArgs);
            }

            args.Add((url, null));

            var arguments = args
                .SelectMany(a => new[] {a.Key, a.Value?.ToString()}.Where(b => !string.IsNullOrEmpty(b))).ToList();
            var regex = new Regex(@"(\d+(\.\d+)?)%");
            var osb = new StringBuilder();
            var esb = new StringBuilder();

            var prevProgress = 0;

            var cmd = Cli.Wrap(LuxBin)
                .WithValidation(CommandResultValidation.None)
                .WithArguments(arguments!, true)
                .WithEnvironmentVariables(new Dictionary<string, string?>
                {
                    {"PATH", FfMpegService.Context.Location}
                })
                .WithStandardOutputPipe(PipeTarget.ToStringBuilder(osb, Encoding.UTF8))
                .WithStandardErrorPipe(new CustomCliWrapPipeTarget(async str =>
                {
                    var match = regex.Match(str);
                    if (match.Success)
                    {
                        var partProgress = (int) decimal.Parse(match.Groups[1].Value);
                        if (prevProgress != partProgress)
                        {
                            prevProgress = partProgress;
                            if (onProgress != null)
                            {
                                await onProgress(partProgress);
                            }
                        }
                    }

                    esb.Append(str);
                }, Encoding.UTF8));
            // It will take several seconds to cancel the CancellationToken if ct was passed here.
            var r = await cmd.ExecuteAsync();

            return new CmdResult(r.ExitCode, osb.ToString(), esb.ToString());
        }
    }
}