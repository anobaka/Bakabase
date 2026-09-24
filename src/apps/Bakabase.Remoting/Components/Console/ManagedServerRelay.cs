using System.Net;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Remoting.Components.Forwarding;
using Bakabase.Remoting.Components.Relay;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Remoting.Components.Console;

/// <summary>
/// What a relay borrows from the app it runs inside. Everything else it builds for itself.
/// </summary>
/// <param name="ManagedStore">The managed-server list; the relay only ever sees a one-entry view of it.</param>
/// <param name="Clock">This server's clock offset, shared with the console that probes it.</param>
/// <param name="SelfAddress">Every listener of this app, so nothing the relay does can point back into the app.</param>
/// <param name="Context">Which server the relay is for, and the switcher behind <c>/client/switcher</c>.</param>
/// <param name="AppService">
/// This app's data directories, for temp playlists and the Locale Emulator components
/// folder. Never published to the page: a relay maps no <c>/client/log</c> or
/// <c>/client/app</c>. Null in a test host.
/// </param>
/// <param name="GuiAdapter">The windows cookie capture opens, on this machine. Null without a GUI.</param>
/// <param name="IdentityVerifier">
/// Asks the server's address who answers there — the console, which knows this device's own
/// identity and ports and records what it hears for the listing.
/// </param>
/// <param name="IdentityPolicy">How often the relay asks, and how long it waits.</param>
public sealed record ManagedServerRelayDependencies(
    ManagedServerStore ManagedStore,
    RelayNavigationTokens Tokens,
    ServerClock Clock,
    ClientSelfAddress SelfAddress,
    ConsoleRelayContext Context,
    ILoggerFactory LoggerFactory,
    AppService? AppService,
    IGuiAdapter? GuiAdapter,
    IUpstreamIdentityVerifier IdentityVerifier,
    UpstreamIdentityPolicy IdentityPolicy);

/// <summary>
/// One managed server's relay: a loopback listener, with a container of its own, that
/// shows the server's UI in this device's window and signs everything it forwards with
/// this device's key for that server.
/// </summary>
/// <remarks>
/// <para>
/// A container per server, not per app, because the relay core resolves "the" connection,
/// "the" clock and "the" key: sharing one container would mean sharing one server. It is
/// also never the app's own container — the user-machine dispatcher in front of every relay
/// intercepts play and open routes, and inside the app's server those routes must keep
/// doing what they do for the app itself.
/// </para>
/// <para>
/// The host is built from nothing the app's own host reads. No configuration source
/// survives (so <c>ASPNETCORE_URLS</c>, <c>appsettings.json</c> and the environment cannot
/// add a listener or change the environment), no logging provider of its own (it logs
/// through the app's), no console lifetime (the app decides when the process stops), and
/// exactly one endpoint: 127.0.0.1 on this server's port.
/// </para>
/// </remarks>
public sealed class ManagedServerRelay(string serverId, int port, ManagedServerRelayDependencies dependencies)
    : IAsyncDisposable
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private WebApplication? _app;
    private int _disposed;

    public string ServerId { get; } = serverId;

    public int Port { get; } = port;

    /// <summary>The relay's own container. Null before <see cref="StartAsync"/>.</summary>
    public IServiceProvider? Services => _app?.Services;

    /// <summary>
    /// Who the relay last found at its server's address, and the way to ask again. Null
    /// before <see cref="StartAsync"/> and after disposal.
    /// </summary>
    public UpstreamIdentity? Identity => _app?.Services.GetService<UpstreamIdentity>();

    /// <summary>
    /// Builds and starts the listener. Throws when the port cannot be bound, which the
    /// console answers by trying another.
    /// </summary>
    public async Task StartAsync(CancellationToken ct)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = [],
            ApplicationName = typeof(ManagedServerRelay).Assembly.GetName().Name,
            ContentRootPath = System.AppContext.BaseDirectory,
            EnvironmentName = Environments.Production
        });

        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        // The app's own factory, which the relay core then wraps to hold back the
        // forwarder's line-per-request chatter (RelayLogging). Registered as an instance so
        // this container never disposes it.
        builder.Services.Replace(ServiceDescriptor.Singleton(dependencies.LoggerFactory));
        builder.Services.Replace(ServiceDescriptor.Singleton<IHostLifetime, OwnedLifetime>());
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = StopTimeout);

        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, Port));

        Compose(builder.Services);

        var app = builder.Build();

        // Nothing but the endpoint above. With the configuration cleared there should be
        // no other address, and this makes that a fact rather than an inference.
        app.Urls.Clear();

        // Never this device's log or directories: the page is the managed server's own
        // code, and this app's log and data directory are this device's whole server — its
        // pairing codes, every other managed server, every key. Those paths reach
        // ConsoleEndpoints' catch-all and answer 404.
        app.UseRelayPipeline(ConsoleEndpoints.Map);

        _app = app;

        try
        {
            await app.StartAsync(ct);
        }
        catch
        {
            _app = null;
            await app.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// The relay's container. Everything registered ahead of the core is something the core
    /// would otherwise make its own — see <see cref="RelayComposition.AddRelayCore"/>.
    /// </summary>
    private void Compose(IServiceCollection services)
    {
        var store = new SingleServerConnectionStore(dependencies.ManagedStore, ServerId);

        services.AddSingleton<IClientConnectionStore>(store);
        services.AddSingleton(dependencies.Tokens);
        services.AddSingleton(dependencies.Clock);
        services.AddSingleton(dependencies.SelfAddress);
        services.AddSingleton(dependencies.Context);
        services.AddSingleton(dependencies.IdentityVerifier);
        services.AddSingleton(dependencies.IdentityPolicy);
        services.AddSingleton<IRelayUnavailablePage, ConsoleUnavailablePageWriter>();

        // Instances rather than factories over the app's container, so this container
        // never disposes something the app still owns.
        if (dependencies.AppService != null)
        {
            services.AddSingleton(dependencies.AppService);
        }

        if (dependencies.GuiAdapter != null)
        {
            services.AddSingleton(dependencies.GuiAdapter);
        }

        services.AddRelayCore(new RelayEnvironment(
            _ => Port,
            dependencies.Context.AppVersion,
            // This app's own temp directory: batch play runs here, on the machine the
            // player is on, whichever server the playlist came from.
            _ => dependencies.AppService?.RequestAppDataDirectory("temp", "playlists") ??
                 Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Bakabase", "playlists")).FullName));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        var app = _app;
        _app = null;

        if (app == null)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(StopTimeout);
            await app.StopAsync(timeout.Token);
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
        {
            // Open streams and hub connections are cut rather than waited on; the relay is
            // going away either way.
        }

        await app.DisposeAsync();
    }

    /// <summary>
    /// A lifetime that listens for nothing. The default one hooks Ctrl+C and SIGTERM, and
    /// one of those per relay would each try to shut its own host down in parallel with the
    /// app's — which already stops every relay it started.
    /// </summary>
    private sealed class OwnedLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
