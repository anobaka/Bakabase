using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Upgrade.Adapters;
using Bakabase.Infrastructures.Components.Jobs;
using Bakabase.Infrastructures.Components.Orm;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Business.Components.CookieValidation;
using Bakabase.InsideWorld.Business.Components.CookieValidation.Infrastructures;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.BakabaseUpdater;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Lux;
using Bakabase.InsideWorld.Business.Components.Downloader;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions;
using Bakabase.InsideWorld.Business.Components.Downloader.DownloaderOptionsValidator;
using Bakabase.InsideWorld.Business.Components.Downloader.Implementations;
using Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Extensions;
using Bakabase.InsideWorld.Business.Components.FileExplorer;
using Bakabase.InsideWorld.Business.Components.FileMover;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bakabase.InsideWorld.Business.Components.Gui.Extensions;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Implementations;
using Bakabase.InsideWorld.Business.Components.ThirdParty.JavLibrary;
using Bakabase.InsideWorld.Business.Configurations;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.Migrations;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Service.Components;
using Bakabase.Service.Components.Tasks;
using Bakabase.Service.Extensions;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Orm.Extensions;
using Bootstrap.Components.Storage.OneDrive;
using Bootstrap.Components.Tasks.Progressor;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service
{
    public class InsideWorldStartup : AppStartup<BakabaseSwaggerCustomModelDocumentFilter>
    {
        public InsideWorldStartup(IConfiguration configuration, IWebHostEnvironment env) : base(configuration, env)
        {
        }

        protected override void ConfigureServicesBeforeOthers(IServiceCollection services)
        {
            services.AddInsideWorldBusinesses();

            //services.TryAddSingleton<SimpleBiliBiliFavoritesCollector>();
            services.AddSingleton<OneDriveService>();

            services.AddBootstrapServices<InsideWorldDbContext>(c =>
                c.UseBootstrapSqLite(AppDataPath, "bakabase_insideworld"));

            services.AddBakabaseHttpClient<BakabaseHttpClientHandler>(InternalOptions.HttpClientNames.Default);

            // services.AddBakabaseHttpClient<JavLibraryThirdPartyHttpMessageHandler>(InternalOptions.HttpClientNames
            //     .JavLibrary);
            // services.AddSimpleProgressor<JavLibraryDownloader>();

            services.AddSingleton<DownloaderManager>();

            services.AddTransient<BilibiliDownloader>();
            services.TryAddSingleton<BilibiliDownloaderOptionsValidator>();
            services.TryAddSingleton<BilibiliCookieValidator>();

            services.AddTransient<ExHentaiSingleWorkDownloader>();
            services.AddTransient<ExHentaiTorrentDownloader>();
            services.AddTransient<ExHentaiListDownloader>();
            services.AddTransient<ExHentaiWatchedDownloader>();
            services.TryAddSingleton<ExHentaiDownloaderOptionsValidator>();
            services.TryAddSingleton<ExHentaiCookieValidator>();

            services.AddTransient<PixivSearchDownloader>();
            services.AddTransient<PixivRankingDownloader>();
            services.AddTransient<PixivFollowingDownloader>();
            services.TryAddSingleton<PixivDownloaderOptionsValidator>();
            services.TryAddSingleton<PixivCookieValidator>();

            services.RegisterAllRegisteredTypeAs<IDownloader>();
            services.RegisterAllRegisteredTypeAs<IDownloaderOptionsValidator>();

            services.AddSingleton<InsideWorldOptionsManagerPool>();

            services.AddSingleton<ThirdPartyHttpRequestLogger>();

            services.AddInsideWorldMigrations();

            services.RegisterAllRegisteredTypeAs<ICookieValidator>();

            services.TryAddSingleton<FfMpegService>();
            services.TryAddSingleton<LuxService>();
            services.TryAddSingleton<BakabaseUpdaterService>();
            services.RegisterAllRegisteredTypeAs<IDependentComponentService>();

            services.TryAddSingleton<IBakabaseUpdater>(sp => sp.GetRequiredService<BakabaseUpdaterService>());

            services.TryAddSingleton<WebGuiHubConfigurationAdapter>();
            services.TryAddSingleton<CompressedFileService>();

            services.AddTransient<IBakabaseLocalizer, InsideWorldLocalizer>(x =>
                x.GetRequiredService<InsideWorldLocalizer>());
            services.AddTransient<IDependencyLocalizer, InsideWorldLocalizer>(x =>
                x.GetRequiredService<InsideWorldLocalizer>());
            services.AddTransient<InsideWorldLocalizer>();

            services.TryAddSingleton<IFileMover, FileMover>();

            services.TryAddSingleton<BakabaseWebProxy>();

            services.AddBTask<BTaskEventHandler>();
            services.AddSingleton<PredefinedTasksProvider>();

            services.AddMediaLibraryTemplate<InsideWorldDbContext>();
        }

        protected override void ConfigureEndpointsAtFirst(IEndpointRouteBuilder routeBuilder)
        {
            routeBuilder.MapHub<WebGuiHub>("/hub/ui");
        }

        protected override void ConfigureCors(CorsPolicyBuilder builder)
        {
            builder.WithOrigins("https://www.north-plus.net", "https://exhentai.org");
        }

        public override void Configure(IApplicationBuilder app, IHostApplicationLifetime lifetime)
        {
            var logger = app.ApplicationServices.GetRequiredService<ILogger<InsideWorldStartup>>();
            logger.LogInformation($"Using app data directory: {AppService.DefaultAppDataDirectory}");

            app.ConfigureDownloadTaskParser();

            // todo: merge gui configuration
            app.ApplicationServices.GetRequiredService<WebGuiHubConfigurationAdapter>().Initialize();
            app.ConfigureGui();

            base.Configure(app, lifetime);
        }
    }
}