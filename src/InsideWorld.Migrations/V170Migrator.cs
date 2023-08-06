using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Migrations;
using Bakabase.Infrastructures.Components.Configurations;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Configs.Infrastructures;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Storage;
using InsideWorld.Migrations.Legacies;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;
using Bakabase.Infrastructures.Components.App.Upgrade.Abstractions;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.InsideWorld.Models.Extensions;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;
using Bakabase.InsideWorld.Models.Configs.Resource;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Configuration.Abstractions;

namespace InsideWorld.Migrations
{
    /// <summary>
    /// Before 1.7.0, There is a large options file, so we'll separate it into multi files.
    /// </summary>
    public sealed class V170Migrator : AbstractMigrator
    {
        private static InsideWorldAppOptions? _prevOptions;
        public V170Migrator(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        protected override string MaxVersionString => "1.6.3";

        private static readonly string PreviousDefaultAppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
#if DEBUG
            $"Bakabase.InsideWorld.App.Debug"
#else
                "Bakabase.InsideWorld.App"
#endif
        );

        public static void CopyCoreAppData()
        {
            // Copy files created by previous versions to new app data folder.
            if (Directory.Exists(PreviousDefaultAppDataDir))
            {
                Directory.CreateDirectory(AppService.DefaultAppDataDirectory);
                DirectoryUtils.CopyFilesRecursively(PreviousDefaultAppDataDir, AppService.DefaultAppDataDirectory,
                    false);
                var newAppOptionsKey = AppOptionsManager.GetAppOptionsKey();
                var appOptionsFilePath = AppOptionsManager.GetAppOptionsFilePath();
                if (File.Exists(appOptionsFilePath))
                {
                    var json = File.ReadAllText(appOptionsFilePath);
                    var jo = JObject.Parse(json);
                    // The previous app.json has not top-level key.
                    if (!jo.ContainsKey(newAppOptionsKey))
                    {
                        _prevOptions =
                            JsonConvert.DeserializeObject<InsideWorldAppOptions>(
                                File.ReadAllText(appOptionsFilePath));
                        var newJo = new JObject
                        {
                            [newAppOptionsKey] = _prevOptions == null ? null : JToken.FromObject(_prevOptions)
                        };
                        File.WriteAllText(appOptionsFilePath, JsonConvert.SerializeObject(newJo, Formatting.Indented),
                            Encoding.UTF8);
                    }
                }
            }
        }

        protected override async Task<object?> MigrateBeforeDbMigrationInternal()
        {
            if (_prevOptions == null)
            {
                return null;
            }
            // App
            await GetRequiredService<IBOptionsManager<AppOptions>>().SaveAsync(t =>
            {
                t.CloseBehavior = _prevOptions.MinimizeOnClose.HasValue
                    ? _prevOptions.MinimizeOnClose.Value ? CloseBehavior.Minimize : CloseBehavior.Exit
                    : CloseBehavior.Prompt;
                t.DataPath = _prevOptions.DataPath;
                t.PrevDataPath = _prevOptions.PrevDataPath;
                t.EnableAnonymousDataTracking = _prevOptions.EnableAnonymousDataTracking;
                t.EnablePreReleaseChannel = _prevOptions.EnablePreReleaseChannel;
                t.Language = _prevOptions.Language;
                t.Version = _prevOptions.Version;
                t.WwwRootPath = _prevOptions.WwwRootPath;
            });

            // Updater
            var updaterOptions = new UpdaterOptions
            {
                AppUpdaterOssObjectPrefix = BusinessConstants.AppOssObjectPrefix
            };
            await GetRequiredService<IBOptionsManager<UpdaterOptions>>().SaveAsync(updaterOptions);

            // Resource
            await GetRequiredService<IBOptionsManager<ResourceOptions>>().SaveAsync(t =>
            {
                t.LastSyncDt = _prevOptions.LastSyncDt;
                t.LastNfoGenerationDt = _prevOptions.LastNfoGenerationDt;
                t.LastSearch = _prevOptions.ResourceSearchRequestModel?.ToOptions();
                t.SearchSlots = _prevOptions.ResourceSearchRequestModelSlot?.Select(a => a.ToOptions()).ToList();
                t.AdditionalCoverDiscoveringSources = _prevOptions.AdditionalCoverDiscoveringSources;
            });

            // UI
            await GetRequiredService<IBOptionsManager<UIOptions>>().SaveAsync(t =>
            {
                t.Resource = new UIOptions.UIResourceOptions {ColCount = _prevOptions.ResourceColCount};
            });

            // ThirdParty
            await GetRequiredService<IBOptionsManager<ThirdPartyOptions>>().SaveAsync(t =>
            {
                t.FFmpeg = new ThirdPartyOptions.FFmpegOptions
                {
                    BinDirectory = _prevOptions.FFmpegBinDirectory
                };
                t.SimpleSearchEngines = _prevOptions.SimpleSearchEngines?.ToList();
            });

            // FileSystem
            await GetRequiredService<IBOptionsManager<FileSystemOptions>>().SaveAsync(t =>
            {
                t.RecentMovingDestinations = _prevOptions.RecentMovingDestinations?.ToArray();
                t.FileProcessor = new FileSystemOptions.FileProcessorOptions()
                    {WorkingDirectory = _prevOptions.FileExplorerWorkingDirectory};
            });

            // Bilibili
            await GetRequiredService<IBOptionsManager<BilibiliOptions>>().SaveAsync(t =>
            {
                t.Cookie = _prevOptions.BiliBiliCookie;
                t.Downloader = new CommonDownloaderOptions
                {
                    DefaultPath = _prevOptions.BiliBiliDownloadPath,
                };
            });

            // ExHentai
            await GetRequiredService<IBOptionsManager<ExHentaiOptions>>().SaveAsync(t =>
            {
                t.Cookie = _prevOptions.ExHentaiCookie;
                t.Downloader = new CommonDownloaderOptions
                {
                    DefaultPath = _prevOptions.ExHentaiDownloadPath,
                    Threads = _prevOptions.ExHentaiDownloadThreads
                };
                t.Enhancer = new ExHentaiOptions.ExHentaiEnhancerOptions
                {
                    ExcludedTags = _prevOptions.ExHentaiExcludedTags
                };
            });

            // JavLibrary
            await GetRequiredService<IBOptionsManager<JavLibraryOptions>>().SaveAsync(t =>
            {
                t.Cookie = _prevOptions.JavLibraryCookie;
                t.Collector = new JavLibraryOptions.CollectorOptions
                {
                    Path = _prevOptions.JavLibraryDownloadPath,
                    TorrentOrLinkKeywords = _prevOptions?.JavLibraryTorrentLinkKeywords?.ToHashSet(),
                    Urls = _prevOptions.JavLibraryUrls?.ToHashSet()
                };
            });

            // // Pixiv
            // await AppService.SaveOptionsAsync<PixivOptions>(t =>
            // {
            //
            // });

            //FileUtils.Delete(appOptionsFilePath, true, true);

            return null;
        }
    }
}