using Bakabase.Client.Remoting.Components.Diagnostics;
using Bakabase.Client.Remoting.Components.Shell;
using Bakabase.Client.Remoting.Components.Updating;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Upgrade.Abstractions;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;
using Bakabase.Remoting.Abstractions;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Remoting.Components.Relay;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AppContext = Bakabase.Infrastructures.Components.App.AppContext;

namespace Bakabase.Client.Remoting.Components.Forwarding;

/// <summary>
/// The legacy thin client's HTTP pipeline: the shared relay, pointed at whichever server
/// this client is connected to, plus the connect page, updater and tray bridge that make
/// it a product of its own.
/// </summary>
/// <remarks>
/// It exists so the frontend needs no notion of being remote. Everything it loads comes
/// from one origin on this machine, which keeps browser storage, the cover-sharding
/// trick and the hub connection working exactly as they do in the all-in-one — while the
/// device key stays in this process and never reaches a page. See
/// <see cref="RelayComposition"/> for the relay itself.
/// </remarks>
public class ClientStartup(IConfiguration configuration, IWebHostEnvironment environment)
{
    public void ConfigureServices(IServiceCollection services)
    {
        ConfigureTelemetry(services);

        // Registered ahead of the relay core, which only fills in what nobody claimed:
        // this product keeps its connection file where it always has.
        services.TryAddSingleton<IClientDataDirectory>(sp =>
            new AppServiceClientDataDirectory(sp.GetRequiredService<AppService>()));

        services.AddRelayCore(new RelayEnvironment(
            sp => ResolveListeningPort(sp.GetRequiredService<AppContext>()),
            AppService.CoreVersion.ToString(),
            // Temp playlists go under this client's own AppData, not the all-in-one's —
            // the two flavours never share a data directory.
            sp => sp.GetRequiredService<AppService>().RequestAppDataDirectory("temp", "playlists")));

        services.AddHttpClient<IServerConnector, ServerConnector>();
        services.AddHttpClient<IClientPairingService, ClientPairingService>();

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
    }

    public void Configure(IApplicationBuilder app, AppContext appContext)
    {
        // Started here rather than in ConfigureServices because the anonymous id lives in
        // the client's data directory, and that is only resolvable once the container is
        // built. A test host reaches this with no DSN configured and does nothing.
        if (ClientTelemetry.IsEnabled(configuration, environment.IsDevelopment()))
        {
            ClientTelemetry.Initialize(configuration, environment.IsDevelopment(), environment.EnvironmentName,
                ClientAnonymousId.GetOrCreate(app.ApplicationServices.GetRequiredService<IClientDataDirectory>()));
        }

        // This program's own log and directories are published to the page as well. Safe
        // here and only here: the thin client runs no server, so its log and its data
        // directory hold nothing but its own connection to the one server it shows. The
        // desktop app's relays leave them out — see UseRelayPipeline.
        app.UseRelayPipeline(endpoints =>
        {
            // Questions about this machine — which server it points at, where that
            // server's libraries are here — which the server has no way to answer.
            ClientApiEndpoints.Map(endpoints, AppService.CoreVersion.ToString());
            ClientUpdaterEndpoints.Map(endpoints);
            ClientTrayEndpoints.Map(endpoints);
        }, mapThisMachinesDiagnostics: true);
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
}
