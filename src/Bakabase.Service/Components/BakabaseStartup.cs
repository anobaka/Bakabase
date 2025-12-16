using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Upgrade.Adapters;
using Bakabase.Infrastructures.Components.Orm;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Business.Components.Configurations;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.BakabaseUpdater;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Lux;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.SevenZip;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.FileMover;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bakabase.InsideWorld.Business.Components.Gui.Extensions;
using Bakabase.InsideWorld.Business.Components.PostParser.Extensions;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.Migrations;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;
using Bakabase.Modules.ThirdParty.Extensions;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bakabase.Modules.ThirdParty.ThirdParties.Pixiv;
using Bakabase.Service.Components.Tasks;
using Bakabase.Service.Extensions;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Orm.Extensions;
using Bootstrap.Components.Storage.OneDrive;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components
{
    public class BakabaseStartup : AppStartup<BakabaseSwaggerCustomModelDocumentFilter>
    {
        public BakabaseStartup(IConfiguration configuration, IWebHostEnvironment env) : base(configuration, env)
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


            services.TryAddSingleton<BilibiliCookieValidator>();
            services.TryAddSingleton<ExHentaiCookieValidator>();
            services.TryAddSingleton<PixivCookieValidator>();

            services.AddSingleton<BakabaseOptionsManagerPool>();

            services.AddSingleton<ThirdPartyHttpRequestLogger>();

            services.AddInsideWorldMigrations();

            services.RegisterAllRegisteredTypeAs<ICookieValidator>();

            services.TryAddSingleton<FfMpegService>();
            services.TryAddSingleton<HardwareAccelerationService>();
            services.TryAddSingleton<LuxService>();
            services.TryAddSingleton<BakabaseUpdaterService>();
            services.TryAddSingleton<SevenZipService>();
            services.RegisterAllRegisteredTypeAs<IDependentComponentService>();

            services.TryAddSingleton<IBakabaseUpdater>(sp => sp.GetRequiredService<BakabaseUpdaterService>());

            services.TryAddSingleton<WebGuiHubConfigurationAdapter>();
            services.TryAddSingleton<CompressedFileService>();

            services.AddTransient<IBakabaseLocalizer, BakabaseLocalizer>(x =>
                x.GetRequiredService<BakabaseLocalizer>());
            services.AddTransient<IDependencyLocalizer, BakabaseLocalizer>(x =>
                x.GetRequiredService<BakabaseLocalizer>());
            services.AddTransient<BakabaseLocalizer>();

            services.TryAddSingleton<IFileMover, FileMover>();

            services.TryAddSingleton<BakabaseWebProxy>();

            services.AddBTask<BTaskEventHandler>();
            services.AddSingleton<DynamicTaskRegistry>();
            services.AddSingleton<PredefinedTasksProvider>();

            services.AddMediaLibraryTemplate<InsideWorldDbContext>();

            // Add version check job that runs 30 seconds after startup
            services.AddHostedService<VersionCheckJob>();
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
            var logger = app.ApplicationServices.GetRequiredService<ILogger<BakabaseStartup>>();
            logger.LogInformation($"Using app data directory: {AppService.DefaultAppDataDirectory}");

            _ = app.ConfigurePostParser();

            // todo: merge gui configuration
            app.ApplicationServices.GetRequiredService<WebGuiHubConfigurationAdapter>().Initialize();
            app.ConfigureGui();

            base.Configure(app, lifetime);
        }
    }
}