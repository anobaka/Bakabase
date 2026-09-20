using System.Net;
using Bakabase.Client.Remoting.Abstractions;
using Bakabase.Client.Remoting.Components.Connection;
using Bakabase.Client.Remoting.Components.Diagnostics;
using Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;
using Bakabase.Client.Remoting.Components.Shell;
using Bakabase.Client.Remoting.Components.Updating;
using Bakabase.Client.Remoting.Components.UserMachine;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Upgrade.Abstractions;
using Bakabase.Client.Remoting.Components.BatchPlay;
using Bakabase.Modules.Player.Abstractions.Components;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;
using Bakabase.Modules.Player.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Extensions;
using Microsoft.Extensions.Options;
using Bakabase.Modules.Player.Components;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AppContext = Bakabase.Infrastructures.Components.App.AppContext;

namespace Bakabase.Client.Remoting.Components.Forwarding;

/// <summary>
/// The client's HTTP pipeline: a loopback listener the embedded browser talks to, which
/// signs and relays to the real server.
/// </summary>
/// <remarks>
/// <para>
/// It exists so the frontend needs no notion of being remote. Everything it loads comes
/// from one origin on this machine, which keeps browser storage, the cover-sharding
/// trick and the hub connection working exactly as they do in the all-in-one — while the
/// device key stays in this process and never reaches a page.
/// </para>
/// <para>
/// Deliberately thin: no response caching, no compression, no buffering, no static
/// files. Each of those would sit between a video stream and the player, and the one
/// thing this layer must not do is get in the way of bytes it is only passing along.
/// </para>
/// </remarks>
public class ClientStartup(IConfiguration configuration, IWebHostEnvironment environment)
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddHttpForwarder();

        ConfigureTelemetry(services);

        services.TryAddSingleton<IClientDataDirectory>(sp =>
            new AppServiceClientDataDirectory(sp.GetRequiredService<AppService>()));
        services.TryAddSingleton<IClientConnectionStore, ClientConnectionStore>();
        services.TryAddSingleton<ServerClock>();
        services.TryAddSingleton<ActiveConnection>();
        services.TryAddSingleton<IUpstreamTarget>(sp => sp.GetRequiredService<ActiveConnection>());
        services.TryAddSingleton<IClientCredentialProvider>(sp => sp.GetRequiredService<ActiveConnection>());
        services.TryAddSingleton<UpstreamTransformer>();
        services.TryAddSingleton(_ => CreateUpstreamInvoker());
        services.TryAddSingleton<UpstreamForwarder>();

        // Its own signed client, for the few calls the client makes on its own behalf
        // rather than on the browser's.
        services.AddHttpClient<IUpstreamContextProbe, UpstreamContextProbe>()
            .AddHttpMessageHandler(sp => new DeviceSigningHandler(
                sp.GetRequiredService<IClientCredentialProvider>(), sp.GetRequiredService<ServerClock>()));

        services.AddHttpClient<IUpstreamApi, UpstreamApi>()
            .AddHttpMessageHandler(sp => new DeviceSigningHandler(
                sp.GetRequiredService<IClientCredentialProvider>(), sp.GetRequiredService<ServerClock>()));

        // The port the host actually bound, so the handshake can recognise an address
        // that points back here. Resolved lazily for the same reason the guard's is: the
        // listening address exists only once the host has started.
        services.TryAddSingleton(sp => new ClientSelfAddress(ResolveListeningPort(sp.GetRequiredService<AppContext>())));

        services.AddHttpClient<IServerConnector, ServerConnector>();
        services.AddHttpClient<IClientPairingService, ClientPairingService>();

        services.TryAddSingleton(sp => new ClientContextEndpoint(
            sp.GetRequiredService<ActiveConnection>(),
            sp.GetRequiredService<IUpstreamContextProbe>(),
            AppService.CoreVersion.ToString()));

        // Finding servers to connect to. Only the connect page uses it, and only before
        // there is a server — after that this client knows exactly where to go. Both
        // channels are registered because they fail on different networks: broadcast is
        // dropped by most enterprise wireless, multicast by plenty of home routers.
        services.TryAddSingleton<UdpProbeClient>();
        services.TryAddSingleton<MdnsBrowser>();
        services.TryAddSingleton<IServerDiscovery, ServerDiscovery>();

        // The client updates itself from its own feed. The server's /updater/* routes are
        // forwarded and still mean "update the server"; these two are different questions
        // about two different programs that happen to share a window.
        services.TryAddSingleton<IAppUpdateSource, ClientUpdateSource>();
        services.AddUpdater();

        // The shell's adapter is the tray icon, and the updater has to hide it before
        // handing over to Velopack. Optional: a host without a GUI simply has none.
        services.TryAddTransient(sp =>
            sp.GetService<IGuiAdapter>() as ITrayIconController);

        // Actions whose effect lands on whatever machine runs them. Anything declared in
        // UserMachineRoutes without a handler here is answered as "this client is
        // behind" rather than forwarded to a server that would only refuse it.
        services.TryAddSingleton<IShellOpener, OsShellOpener>();
        services.AddSingleton<IUserMachineHandler, OpenUrlHandler>();
        services.AddSingleton<IUserMachineHandler, OpenPathHandler>();
        services.AddSingleton<IUserMachineHandler, OpenFileHandler>();
        services.AddSingleton<IUserMachineHandler, OpenResourceDirectoryHandler>();
        services.TryAddSingleton<IPlayerExecutableLocator, DefaultPlayerExecutableLocator>();
        services.TryAddSingleton<LocalPlayerResolver>();
        services.TryAddSingleton<ILoopbackAddressProvider>(sp =>
            new LoopbackAddressProvider(ResolveListeningPort(sp.GetRequiredService<AppContext>())));
        services.TryAddSingleton<LocalPlayback>();
        services.AddSingleton<IUserMachineHandler, PlayItemHandler>();
        services.AddSingleton<IUserMachineHandler, PlayResourceHandler>();
        services.AddSingleton<IUserMachineHandler, PlayRandomResourceHandler>();

        // Batch play runs the server's own orchestration in this process, with the
        // library read over HTTP and the files resolved against this machine. Registered
        // ahead of AddPlayerModule, whose defaults assume both are local and which only
        // fills in what nobody claimed.
        services.TryAddScoped<IBatchPlayResourceSource, UpstreamBatchPlayResourceSource>();
        services.TryAddScoped<IBatchPlayPlaylistSource, UpstreamBatchPlayPlaylistSource>();
        services.TryAddSingleton<IBatchPlayFileResolver>(sp => new ClientBatchPlayFileResolver(
            sp.GetRequiredService<ActiveConnection>(), sp.GetRequiredService<ILoopbackAddressProvider>()));
        services.AddPlayerModule();

        // Temp playlists go under this client's own AppData, not the all-in-one's — the
        // two flavours never share a data directory.
        services.AddSingleton<IConfigureOptions<PlayerModuleOptions>>(sp =>
            new ConfigureOptions<PlayerModuleOptions>(o =>
                o.TempPlaylistDirectory = sp.GetRequiredService<AppService>()
                    .RequestAppDataDirectory("temp", "playlists")));

        services.AddSingleton<IUserMachineHandler, BatchPlayCandidatesHandler>();
        services.AddSingleton<IUserMachineHandler, BatchPlayResourcesHandler>();
        services.AddSingleton<IUserMachineHandler, PlaylistBatchPlayCandidatesHandler>();
        services.AddSingleton<IUserMachineHandler, PlaylistBatchPlayHandler>();
        services.AddSingleton<IUserMachineHandler, RecycleBinHandler>();
        services.AddSingleton<IUserMachineHandler, FileIconHandler>();
        services.AddSingleton<IUserMachineHandler, TampermonkeyInstallHandler>();
        services.AddSingleton<IUserMachineHandler, OpenAigcArtifactHandler>();
        services.TryAddSingleton<ILocaleEmulatorLauncher, LocaleEmulatorLauncher>();
        services.AddSingleton<IUserMachineHandler, DLsiteLaunchHandler>();

        // Signing in to a third-party site opens a window, so it has to open here. The
        // flows and the orchestration are the server's own; only the window is local.
        services.AddLocalization();
        services.TryAddTransient<ICookieCaptureLocalizer, ThirdPartyCookieCaptureLocalizer>();
        services.TryAddTransient<CookieCaptureOrchestrator>();
        foreach (var flow in typeof(ICookieCaptureFlow).Assembly.GetTypes()
                     .Where(t => t is {IsAbstract: false, IsInterface: false} &&
                                 typeof(ICookieCaptureFlow).IsAssignableFrom(t)))
        {
            services.AddTransient(typeof(ICookieCaptureFlow), flow);
        }

        services.AddSingleton<IUserMachineHandler, CookieCaptureHandler>();
        services.TryAddSingleton<UserMachineDispatcher>();

        services.AddRouting();
    }

    public void Configure(IApplicationBuilder app, AppContext appContext, ILogger<ClientStartup> logger)
    {
        // Started here rather than in ConfigureServices because the anonymous id lives in
        // the client's data directory, and that is only resolvable once the container is
        // built. A test host reaches this with no DSN configured and does nothing.
        if (ClientTelemetry.IsEnabled(configuration, environment.IsDevelopment()))
        {
            ClientTelemetry.Initialize(configuration, environment.IsDevelopment(), environment.EnvironmentName,
                ClientAnonymousId.GetOrCreate(app.ApplicationServices.GetRequiredService<IClientDataDirectory>()));
        }

        // The port comes from the address the host actually bound, not from a second
        // copy of the same decision — the guard has to be right about it or it either
        // refuses everything or protects nothing.
        var port = ResolveListeningPort(appContext);
        var guard = new LoopbackOriginGuard(port);

        app.Use(async (context, next) =>
        {
            var verdict = guard.Evaluate(context.Request.Host.Value, context.Request.Headers.Origin.ToString(),
                context.Request.Method);

            if (verdict != LoopbackGuardVerdict.Allowed)
            {
                logger.LogWarning("Refused {Verdict} request {Method} {Path} (Host: {Host}, Origin: {Origin})",
                    verdict, context.Request.Method, context.Request.Path, context.Request.Host.Value,
                    context.Request.Headers.Origin.ToString());

                context.Response.StatusCode = (int) HttpStatusCode.BadRequest;
                context.Response.Headers["X-Bakabase-Client"] = ClientForwardingFailure.ForeignCaller.ToString();
                await context.Response.WriteAsync("This address only serves Bakabase's own window.");
                return;
            }

            await next();
        });

        // Ahead of routing, because these are the server's routes — the client is
        // intercepting them, not defining its own.
        app.Use(async (context, next) =>
        {
            var dispatcher = context.RequestServices.GetRequiredService<UserMachineDispatcher>();

            if (!await dispatcher.TryHandleAsync(context))
            {
                await next();
            }
        });

        app.UseRouting();

        app.UseEndpoints(endpoints =>
        {
            // Answered here rather than upstream: the question is about the client, and
            // only the client knows the answer.
            endpoints.MapGet(ClientContextEndpoint.Path,
                (HttpContext context, ClientContextEndpoint endpoint) => endpoint.WriteAsync(context));

            // Questions about this machine — which server it points at, where that
            // server's libraries are here — which the server has no way to answer.
            ClientApiEndpoints.Map(endpoints, AppService.CoreVersion.ToString());
            ClientUpdaterEndpoints.Map(endpoints);
            ClientLogEndpoints.Map(endpoints);
            ClientAppEndpoints.Map(endpoints);
            ClientTrayEndpoints.Map(endpoints);

            // Everything else is the server's. Actions that have to run on this machine
            // are still refused upstream, with a reason saying so, until the client
            // learns to run them itself — so nothing silently happens on the wrong
            // computer in the meantime.
            //
            // The pattern is spelled out because MapFallback's default one is
            // "{*path:nonfile}", and nonfile excludes every path whose last segment
            // contains a dot. That would drop the entire frontend on the floor — the
            // server sends it as /assets/index-<hash>.js and friends — and leave the
            // window blank with a 404 per asset.
            endpoints.MapFallback("/{**path}", (HttpContext context, UpstreamForwarder forwarder) =>
                forwarder.ForwardAsync(context));
        });
    }

    /// <summary>
    /// Routes this process's own error logs to Sentry, if this build has somewhere to
    /// report to.
    /// </summary>
    /// <remarks>
    /// The SDK itself is started in <see cref="Configure"/>, where the client's data
    /// directory can be resolved for the anonymous id. Registered here behind the same
    /// condition so the provider is never attached to a hub that will not exist.
    /// </remarks>
    private void ConfigureTelemetry(IServiceCollection services)
    {
        if (!ClientTelemetry.IsEnabled(configuration, environment.IsDevelopment()))
        {
            return;
        }

        // Same reasoning as the server's: the static Serilog logger is built inside
        // Bakabase.Infrastructures, so the provider that reaches Sentry is the
        // Microsoft.Extensions.Logging one.
        services.AddLogging(builder => builder.AddSentry(o =>
        {
            o.InitializeSdk = false;
            o.MinimumEventLevel = LogLevel.Error;
            o.MinimumBreadcrumbLevel = LogLevel.Information;
        }));
    }

    private static int ResolveListeningPort(AppContext appContext)
    {
        var address = appContext.ListeningAddresses?.FirstOrDefault();

        if (address != null && Uri.TryCreate(address, UriKind.Absolute, out var parsed) && parsed.Port > 0)
        {
            return parsed.Port;
        }

        throw new InvalidOperationException(
            "The client host reported no listening address, so the loopback guard cannot be armed.");
    }

    /// <summary>
    /// The invoker YARP relays through. Everything that would normally be helpful is
    /// turned off: automatic decompression would break a byte-range video, cookies would
    /// mix the browser's with the client's, and following redirects would hide the
    /// server's own answer.
    /// </summary>
    private static HttpMessageInvoker CreateUpstreamInvoker() =>
        new(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            // Long-lived by design: this carries the hub connection and video streams.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ActivityHeadersPropagator = null
        });
}
