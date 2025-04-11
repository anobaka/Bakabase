using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Aliyun.OSS;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Upgrade;
using Bakabase.Infrastructures.Components.App.Upgrade.Abstractions;
using Bakabase.Infrastructures.Components.App.Upgrade.Adapters;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Storage;
using Bootstrap.Components.Tasks;
using Bootstrap.Extensions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Semver;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.BakabaseUpdater
{
    public class BakabaseUpdaterService : HttpSourceComponentService, IBakabaseUpdater
    {
        public override string Id => "274f8ab5-a0a6-4415-8466-137d986d3ddb";
        protected override string KeyInLocalizer => "Bakabase.Updater";
        private readonly IBOptions<UpdaterOptions> _options;
        private const string OssPrefix = "app/bakabase/updater/";
        public override bool IsRequired => true;
        private OssClient? _ossClient;
        private static readonly object OssClientInitializationLock = new object();
        private readonly IHubContext<WebGuiHub, IWebGuiClient> _uiHub;

        protected string Executable => GetExecutableWithValidation("Bakabase.Updater");

        public BakabaseUpdaterService(ILoggerFactory loggerFactory, AppService appService,
            IBOptions<UpdaterOptions> options,
            IHttpClientFactory httpClientFactory, IHubContext<WebGuiHub, IWebGuiClient> uiHub,
            IServiceProvider globalServiceProvider) :
            base(loggerFactory, appService, "updater", httpClientFactory, globalServiceProvider)
        {
            _options = options;
            _uiHub = uiHub;
        }

        protected OssClient OssClient
        {
            get
            {
                if (_ossClient == null)
                {
                    lock (OssClientInitializationLock)
                    {
                        if (_ossClient == null)
                        {
                            if (_options.Value.OssEndpoint.IsNotEmpty() &&
                                _options.Value.OssAccessKeyId.IsNotEmpty() &&
                                _options.Value.OssAccessKeySecret.IsNotEmpty())
                            {
                                _ossClient = new OssClient(_options.Value.OssEndpoint,
                                    _options.Value.OssAccessKeyId, _options.Value.OssAccessKeySecret);
                            }
                        }
                    }
                }

                if (_ossClient == null)
                {
                    throw new Exception(
                        $"Failed to initialize oss client by options: {JsonConvert.SerializeObject(_options.Value)}");
                }

                return _ossClient;
            }
        }

        public override async Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct)
        {
            var versionPaths = OssClient.ListObjects(new ListObjectsRequest(_options.Value.OssBucket)
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
                }).Where(a => a.Version != null).OrderByDescending(a => a.Version!, SemVersion.SortOrderComparer).ToList();

            var semVer = prefixAndVersions.Any() ? prefixAndVersions.FirstOrDefault().Version : null;
            if (semVer == null)
            {
                throw new Exception($"Can not find any version of {DisplayName}");
            }

            var version = semVer.ToString();

            var installersPrefix = $"{OssPrefix.TrimEnd('/')}/{version}/installer/";
            var installers = OssClient
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
                result = OssClient.ListObjects(listObjectsRequest);
                remoteFiles.AddRange(result.ObjectSummaries.Where(a => a.Size > 0));
            } while (result.IsTruncated);

            return remoteFiles.ToArray();
        }

        protected override async Task PostDownloading(List<string> files, CancellationToken ct)
        {
            await DirectoryUtils.MoveAsync(TempDirectory, DefaultLocation, true, null, PauseToken.None, ct);
        }

        protected override IDiscoverer Discoverer { get; } = new BakabaseUpdaterDiscover();

        public Task StartUpdater(int pid, string processName, string appDir, string newFilesDir,
            string executable, AppVersionInfo.Installer? installer)
        {
            var rawArguments = new List<object>
            {
                "--pid",
                pid,
                "--process-name",
                processName,
                "--app-dir",
                appDir,
                "--new-files-dir",
                newFilesDir,
                "--executable",
                executable,
                "--culture",
                CultureInfo.CurrentUICulture.Name
            };

            if (installer != null)
            {
                rawArguments.Add("--installer");
                rawArguments.Add($"{installer.ToCommand()}");
            }

            var arguments = rawArguments.Select(a => $"\"{a}\"").ToArray();

            var argumentsString = string.Join(' ', arguments);

            var startInfo = new ProcessStartInfo(Executable, argumentsString)
            {
                Verb = "runas",
                CreateNoWindow = true
            };

            Logger.LogInformation($"Starting {Executable} with arguments {argumentsString}");

            Process.Start(startInfo);

            return Task.CompletedTask;
        }
    }
}