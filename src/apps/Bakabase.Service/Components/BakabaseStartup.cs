using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Services;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Upgrade.Abstractions;
using Bakabase.Infrastructures.Components.Orm;
using Bakabase.InsideWorld.Business;
using Sentry;
using Sentry.Extensions.Logging;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Business.Components.CookieCapture;
using Bakabase.InsideWorld.Business.Components.Configurations;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Lux;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.LocaleEmulator;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.SevenZip;
using Bakabase.InsideWorld.Business.Components.FileMover;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bakabase.InsideWorld.Business.Components.Gui.Extensions;
using Bakabase.InsideWorld.Business.Components.PostParser.Extensions;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.Migrations;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili;
using Bakabase.Modules.ThirdParty.ThirdParties.DLsite;
using Bakabase.Modules.ThirdParty.ThirdParties.DLsite;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bakabase.Modules.ThirdParty.ThirdParties.Pixiv;
using Bakabase.Modules.ThirdParty.ThirdParties.Fanbox;
using Bakabase.Modules.ThirdParty.ThirdParties.Fantia;
using Bakabase.Modules.ThirdParty.ThirdParties.Cien;
using Bakabase.Modules.ThirdParty.ThirdParties.Patreon;
using Bakabase.Modules.ThirdParty.ThirdParties.Bangumi;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;
using Bakabase.Service.Components.Tasks;
using Bakabase.Service.Extensions;
using Bakabase.Service.Services;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Orm.Extensions;
using Bootstrap.Components.Storage.OneDrive;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
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

            // The Velopack feed URL is a Bakabase deployment detail, kept out of the
            // generic Bakabase.Infrastructures lib. AppUpdater consumes it via the
            // IAppUpdateSource abstraction.
            services.AddSingleton<IAppUpdateSource, BakabaseUpdateSource>();

            //services.TryAddSingleton<SimpleBiliBiliFavoritesCollector>();
            services.AddSingleton<OneDriveService>();

            services.AddBootstrapServices<BakabaseDbContext>(c =>
                c.UseBootstrapSqLite(AppDataPath, "bakabase_insideworld"));

            services.AddBakabaseHttpClient<BakabaseHttpClientHandler>(InternalOptions.HttpClientNames.Default);

            // services.AddBakabaseHttpClient<JavLibraryThirdPartyHttpMessageHandler>(InternalOptions.HttpClientNames
            //     .JavLibrary);
            // services.AddSimpleProgressor<JavLibraryDownloader>();


            services.TryAddSingleton<BilibiliCookieValidator>();
            services.TryAddSingleton<DLsiteCookieValidator>();
            services.TryAddSingleton<ExHentaiCookieValidator>();
            services.TryAddSingleton<PixivCookieValidator>();
            services.TryAddSingleton<FanboxCookieValidator>();
            services.TryAddSingleton<FantiaCookieValidator>();
            services.TryAddSingleton<CienCookieValidator>();
            services.TryAddSingleton<PatreonCookieValidator>();
            services.TryAddSingleton<SoulPlusCookieValidator>();
            services.TryAddSingleton<BangumiCookieValidator>();

            services.AddSingleton<BakabaseOptionsManagerPool>();

            services.AddSingleton<ThirdPartyHttpRequestLogger>();

            services.AddBakabaseMigrations();

            services.RegisterAllRegisteredTypeAs<ICookieValidator>();

            services.TryAddSingleton<BilibiliCookieCaptureFlow>();
            services.TryAddSingleton<ExHentaiCookieCaptureFlow>();
            services.TryAddSingleton<DLsiteCookieCaptureFlow>();
            services.TryAddSingleton<PixivCookieCaptureFlow>();
            services.TryAddSingleton<FanboxCookieCaptureFlow>();
            services.TryAddSingleton<FantiaCookieCaptureFlow>();
            services.TryAddSingleton<CienCookieCaptureFlow>();
            services.TryAddSingleton<PatreonCookieCaptureFlow>();
            services.TryAddSingleton<BangumiCookieCaptureFlow>();
            services.TryAddSingleton<SoulPlusCookieCaptureFlow>();
            services.RegisterAllRegisteredTypeAs<ICookieCaptureFlow>();
            services.AddTransient<CookieCaptureOrchestrator>();

            services.TryAddSingleton<FfMpegService>();
            services.TryAddSingleton<HardwareAccelerationService>();
            services.TryAddSingleton<LuxService>();
            services.TryAddSingleton<SevenZipService>();
            services.TryAddSingleton<LocaleEmulatorService>();
            services.RegisterAllRegisteredTypeAs<IDependentComponentService>();

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
            services.AddTransient(sp =>
                sp.GetService<Bakabase.Infrastructures.Components.Gui.IGuiAdapter>()
                    as Bakabase.Abstractions.Components.Gui.ITrayIconController);
            services.AddSingleton<DynamicTaskRegistry>();
            services.AddSingleton<IPrepareCacheTrigger, PrepareCacheTrigger>();

            services.AddMediaLibraryTemplate<BakabaseDbContext>();

            // Add version check job that runs 30 seconds after startup
            services.AddHostedService<VersionCheckJob>();

            // Add path mark expiration check job that periodically checks for expired marks
            services.AddHostedService<PathMarkExpirationCheckJob>();

            // Add resource discovery service for SSE-based cover and playable files discovery
            services.AddSingleton<ResourceDiscoveryService>();
            services.AddHostedService(sp => sp.GetRequiredService<ResourceDiscoveryService>());

            // Legacy install AppData detector — fires the dismissable notice on startup.
            // Detection state is held in a singleton so late-connecting hub clients can pick
            // up the notice via WebGuiHub.GetInitialData (rather than relying solely on a
            // one-time SignalR push that races with client connect time).
            services.AddSingleton<Bakabase.Infrastructures.Components.App.LegacyInstallNoticeState>();
            services.AddSingleton<Bakabase.Infrastructures.Components.App.ILegacyInstallNotifier,
                HubLegacyInstallNotifier>();
            services.AddHostedService<Bakabase.Infrastructures.Components.App.LegacyInstallAppDataDetector>();

            // Add MiniProfiler for performance tracking
            services.AddMiniProfiler(options =>
            {
                // Route for profiler results: /profiler/results-index
                options.RouteBasePath = "/profiler";
                // Only profile in development or when explicitly enabled
                options.ShouldProfile = _ => true;
                // Track SQL if using EF
                options.TrackConnectionOpenClose = true;
            });

            // Analytics (Clarity / GA4 / Sentry) — credentials bound from the "Analytics"
            // section of appsettings.json + ENV overrides. The device-id service emits a
            // stable per-install UUID that all three SDKs share, so frontend recordings,
            // backend errors, and behaviour events can be cross-referenced for the same user.
            services.Configure<AnalyticsConfiguration>(Configuration.GetSection("Analytics"));
            services.AddSingleton<IDeviceIdService, DeviceIdService>();
            // Scoped because TelemetrySnapshotService consumes the scoped BakabaseDbContext.
            services.AddScoped<ITelemetrySnapshotService, TelemetrySnapshotService>();

            // Sentry backend SDK. Initialised programmatically (rather than via the more
            // common WebHostBuilder.UseSentry() pattern) because our IHostBuilder is built
            // by the Bakabase.Infrastructures library and we don't have a clean hook there.
            // SentrySdk.Init wires up the global unhandled-exception handlers; the
            // Sentry.AspNetCore HostingStartup attribute auto-attaches the diagnostic
            // listener that captures HTTP request errors. Backend DSN is intentionally
            // separate from the frontend's (per design decision §16, item 2 — two projects).
            var backendSentryDsn = Configuration["Analytics:Sentry:BackendDsn"];
            if (!string.IsNullOrWhiteSpace(backendSentryDsn))
            {
                SentrySdk.Init(o =>
                {
                    o.Dsn = backendSentryDsn;
                    o.Release = AppService.CoreVersion.ToString();
                    o.Environment = Env.EnvironmentName;
                    // Performance tracing off — we want errors only, to stay within free tier.
                    o.TracesSampleRate = 0;
                    // We already log every failed third-party HTTP request via
                    // the scraper's own try/catch and surface them as user-facing
                    // messages. Sentry's HttpClient auto-handler turns each
                    // non-2xx response from CableAV / Fc2 / etc. into a separate
                    // event, which is noise — turn it off.
                    o.CaptureFailedRequests = false;
                    // Drop a couple of known-benign exceptions before they hit
                    // Sentry, otherwise they drown out actionable errors:
                    //   * OperationCanceledException — ASP.NET's signal for a
                    //     client disconnect (tab closed, navigated away).
                    //   * ArgumentOutOfRangeException from ResponseCaching's
                    //     MemoryResponseCache.Set when the framework hands it
                    //     a TimeSpan.Zero validFor (a long-standing bug; the
                    //     request itself still completes).
                    o.SetBeforeSend(@event =>
                    {
                        var ex = @event.Exception;
                        while (ex != null)
                        {
                            if (ex is OperationCanceledException)
                            {
                                return null;
                            }
                            if (ex is ArgumentOutOfRangeException aoore &&
                                aoore.ParamName == "AbsoluteExpirationRelativeToNow")
                            {
                                return null;
                            }
                            ex = ex.InnerException;
                        }
                        return @event;
                    });
                });
                SentrySdk.ConfigureScope(scope =>
                {
                    scope.SetTag("release_channel", ReleaseChannelDetector.Detect(Env));
                });

                // Forward Microsoft.Extensions.Logging output into Sentry so any
                // `_logger.LogError(...)` / `_logger.LogCritical(...)` flows into the
                // same dashboard as unhandled exceptions. Lower levels become
                // breadcrumbs to give context to subsequent events.
                //
                // We use MSEL rather than Serilog because the static Serilog logger is
                // constructed inside Bakabase.Infrastructures (submodule we don't own)
                // and threading a Sentry sink through there would require an upstream
                // change. The MSEL provider is independent and works regardless.
                services.AddLogging(builder => builder.AddSentry(o =>
                {
                    o.InitializeSdk = false; // SentrySdk.Init above already did this
                    o.MinimumEventLevel = LogLevel.Error;
                    o.MinimumBreadcrumbLevel = LogLevel.Information;
                }));
            }
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
            var appService = app.ApplicationServices.GetRequiredService<AppService>();
            logger.LogInformation(
                "AppData anchor: {Anchor}; effective data dir: {Data} (source: {Source})",
                appService.AnchorPath,
                appService.AppDataDirectory,
                appService.DataPathSource);

            // Attach the anonymous device id to the Sentry global scope. SentryHub treats
            // user.id specially for "Crash-free Users" calculations.
            if (SentrySdk.IsEnabled)
            {
                var deviceId = app.ApplicationServices.GetRequiredService<IDeviceIdService>().GetOrCreate();
                SentrySdk.ConfigureScope(scope => scope.User = new SentryUser { Id = deviceId });
            }

            // Enable MiniProfiler - should be early in the pipeline
            app.UseMiniProfiler();

            _ = app.ConfigurePostParser();

            // todo: merge gui configuration
            app.ApplicationServices.GetRequiredService<WebGuiHubConfigurationAdapter>().Initialize();
            app.ConfigureGui();

            base.Configure(app, lifetime);
        }
    }
}