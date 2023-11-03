using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aliyun.OSS;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Upgrade;
using Bakabase.Infrastructures.Components.App.Upgrade.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Storage;
using ElectronNET.API;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Semver;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Updater
{
    public class UpdaterService : HttpSourceComponentService
    {
        public override string Id => "274f8ab5-a0a6-4415-8466-137d986d3ddb";
        public override string DisplayName => "Bakabase.Updater";
        public override string? Description => string.Empty;
        private readonly IBOptions<UpdaterOptions> _options;
        private const string OssPrefix = "app/bakabase/updater/";
        public override bool IsRequired => true;
        private readonly OssClient _ossClient = null!;

        protected string Executable => GetExecutableWithValidation("Bakabase.Updater");

        public UpdaterService(ILoggerFactory loggerFactory, AppService appService, IBOptions<UpdaterOptions> options,
            IHttpClientFactory httpClientFactory) :
            base(loggerFactory, appService, "updater", httpClientFactory)
        {
            _options = options;
            try
            {
                _ossClient = new OssClient(_options.Value.OssEndpoint,
                    _options.Value.OssAccessKeyId, _options.Value.OssAccessKeySecret);
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Can not initialize oss client: {e.Message}");
            }
        }

        public override async Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct)
        {
            var versionPaths = _ossClient.ListObjects(new ListObjectsRequest(_options.Value.OssBucket)
            {
                Prefix = OssPrefix,
                Delimiter = "/"
            });
            var prefixAndVersions = versionPaths.CommonPrefixes.Where(a => a.EndsWith('/'))
                .Select(a =>
                {
                    var versionString = Path.GetFileName(a.TrimEnd('/').TrimStart('v'));
                    return (Prefix: a,
                        Version: SemVersion.TryParse(versionString, SemVersionStyles.Any, out var v) ? v : null);
                }).Where(a => a.Version != null).OrderByDescending(a => a.Version).ToList();

            var semVer = prefixAndVersions.Any() ? prefixAndVersions.FirstOrDefault().Version : null;
            if (semVer == null)
            {
                throw new Exception($"Can not find any version of {DisplayName}");
            }

            var version = semVer.ToString();

            var installersPrefix = $"{OssPrefix.TrimEnd('/')}/{version}/installer/";
            var installers = _ossClient
                .ListObjects(new ListObjectsRequest(_options.Value.OssBucket)
                    {Prefix = installersPrefix}).ObjectSummaries.Where(a => a.Size > 0).Select(a =>
                    new AppVersionInfo.Installer
                    {
                        Name = Path.GetFileName(a.Key),
                        Size = a.Size,
                        Url =
                            $"{_options.Value.OssDomain.TrimEnd('/')}/{string.Join('/', a.Key.Split('/').Select(Uri.EscapeDataString))}"
                    }).ToArray();

            return new DependentComponentVersion
            {
                Version = version!,
                CanUpdate = Context?.Version != version
            };
        }

        protected override async Task<Dictionary<string, string>> GetDownloadUrls(DependentComponentVersion version,
            CancellationToken ct)
        {
            var appPrefix = $"{OssPrefix.TrimEnd('/')}/{version.Version}/";
            var remoteFiles = ListOssObjects(appPrefix);

            var files = remoteFiles.Where(a => a.Size > 0).Select(a =>
            {
                var key = a.Key;
                var url = $"{_options.Value.OssDomain.TrimEnd('/')}/{key.TrimStart('/')}";
                var fileName = key.Replace(appPrefix, null);
                return (Url: url, FileName: fileName);
            }).ToDictionary(a => a.Url, a => a.FileName);

            return files;
        }

        private OssObjectSummary[] ListOssObjects(string prefix)
        {
            var remoteFiles = new List<OssObjectSummary>();
            const int pageSize = 1000;
            ObjectListing result = null;
            do
            {
                // 每页列举的文件个数通过mMxKeys指定，超出指定数量的文件将分页显示。
                var listObjectsRequest = new ListObjectsRequest(_options.Value.OssBucket)
                {
                    Prefix = prefix,
                    Marker = result?.NextMarker,
                    MaxKeys = pageSize
                };
                result = _ossClient.ListObjects(listObjectsRequest);
                remoteFiles.AddRange(result.ObjectSummaries.Where(a => a.Size > 0));
            } while (result.IsTruncated);

            return remoteFiles.ToArray();
        }

        protected override async Task PostDownloading(List<string> files, CancellationToken ct)
        {
            await DirectoryUtils.MergeAsync(TempDirectory, DefaultLocation, true, null, ct);
        }

        protected override IDiscoverer Discoverer { get; } = new UpdaterDiscover();

        public async Task PrepareNewAppVersion()
        {
            AppUpdater appUpdater;

            await appUpdater.StartUpdating();
        }

        public async Task UpdateAndRestartApp()
        {

            var newVersion = await CheckNewVersion();
            var updaterExecutable = Path.Combine(AppService.AppDataDirectory, "updater", "Bakabase.Updater.exe");
            if (!File.Exists(updaterExecutable))
            {
                throw new Exception($"{updaterExecutable} does not exits");
            }

            newVersion.Installers[0].OsPlatform = OSPlatform.Windows;
            newVersion.Installers[0].OsArchitecture = Architecture.X64;

            var installer = newVersion.Installers?.FirstOrDefault(a =>
                a.OsPlatform != null && RuntimeInformation.IsOSPlatform(a.OsPlatform.Value) &&
                RuntimeInformation.OSArchitecture == a.OsArchitecture);

            var process = Process.GetCurrentProcess();

            var rawArguments = new List<object>
            {
                "--pid",
                process.Id,
                "--process-name",
                process.ProcessName,
                "--app-dir",
                Path.GetDirectoryName(process.MainModule!.FileName)!,
                "--new-files-dir",
                DownloadDir,
                "--executable",
                process.MainModule!.FileName!
            };

            if (installer != null)
            {
                rawArguments.Add("--installer");
                rawArguments.Add($"{installer.ToCommand()}");
            }

            var arguments = rawArguments.Select(a => $"\"{a}\"").ToArray();

            var argumentsString = string.Join(' ', arguments);

            var startInfo = new ProcessStartInfo(updaterExecutable, argumentsString)
            {
                Verb = "runas",
                CreateNoWindow = true
            };
        }
    }
}