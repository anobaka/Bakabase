using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Bakabase.Abstractions.Components.Gui;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Remoting.Components.UserMachine;
using Bakabase.Remoting.Components.BatchPlay;
using Microsoft.AspNetCore.Http;
using Bakabase.Abstractions.Services;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Modules.Player.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;
using Bakabase.Modules.RemoteAccess.Components.Management;
using Bakabase.Remoting.Abstractions.Models;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Remoting.Components.Forwarding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AppContext = Bakabase.Infrastructures.Components.App.AppContext;

namespace Bakabase.Remoting.Components.Console;

/// <summary>Desktop-only relays around the shared management connection service.</summary>
public sealed class RemoteConsoleManager : ManagedServerManager, IMainViewSwitcher, IRemoteConsoleNavigator
{
    private const int MaxBindAttempts = 4;
    private readonly RemoteConsoleOptions _consoleOptions;
    private readonly RemoteConsoleLocalOrigin _localOrigin;
    private readonly RelayNavigationTokens _tokens;
    private readonly IServiceProvider _services;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SemaphoreSlim _relayGate = new(1, 1);
    private readonly ConcurrentDictionary<string, ManagedServerRelay> _relays = new(StringComparer.Ordinal);

    public RemoteConsoleManager(RemoteConsoleOptions options, ManagedServerStore store,
        IRemoteAccessService remoteAccess, RemoteConsoleLocalOrigin localOrigin,
        RelayNavigationTokens tokens, RemoteConsoleSwitcher switcher, IServerDiscovery discovery,
        IServiceProvider services, ILoggerFactory loggerFactory)
        : base(options, store, remoteAccess, discovery, loggerFactory)
    {
        _consoleOptions = options;
        _localOrigin = localOrigin;
        _tokens = tokens;
        _services = services;
        _loggerFactory = loggerFactory;
        switcher.Attach(this);
    }

    public IReadOnlyDictionary<string, int> RunningRelays =>
        _relays.ToDictionary(r => r.Key, r => r.Value.Port, StringComparer.Ordinal);
    internal IServiceProvider? RelayServices(string serverId) =>
        _relays.TryGetValue(serverId, out var relay) ? relay.Services : null;
    public override string LocalName => RemoteConsoleSwitcher.LocalName;
    protected override bool CanOpenDesktopView => true;
    public string? LocalOrigin => _localOrigin.Origin;

    protected override async Task StopAllRelaysAsync()
    {
        await _relayGate.WaitAsync(CancellationToken.None);
        try
        {
            await Task.WhenAll(_relays.Values.Select(relay => relay.DisposeAsync().AsTask()));
            _relays.Clear();
        }
        finally
        {
            _relayGate.Release();
        }
    }

    #region IManagedServerService

    public override async Task<ManagedServerResponse> PlayManagedResourceAsync(string serverId, int resourceId,
        DataOrigin origin, string key, string libraryEpoch, bool explicitChoice = false, CancellationToken ct = default)
    {
        if (resourceId <= 0 || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(libraryEpoch))
            throw new ArgumentException("A resource, playable item and source library identity are required.");
        var (relay, _) = await EnsureRelayAsync(serverId, ct);
        if (relay?.Services == null) throw new InvalidOperationException("This server is no longer managed.");
        var identity = await relay.Identity!.EnsureAsync(ct);
        if (identity?.IsConfirmed != true) throw new InvalidOperationException("The source device could not be verified.");
        using var output = new MemoryStream();
        var context = new DefaultHttpContext {RequestServices = relay.Services, RequestAborted = ct};
        context.Response.Body = output;
        context.Request.QueryString = QueryString.Create(new Dictionary<string, string?>
        {
            ["managedLibraryEpoch"] = libraryEpoch,
            ["explicitChoice"] = explicitChoice ? "true" : "false"
        });
        await relay.Services.GetRequiredService<LocalPlayback>().PlayAsync(context, resourceId, origin, key);
        return new ManagedServerResponse(context.Response.StatusCode, "application/json",
            System.Text.Encoding.UTF8.GetString(output.ToArray()));
    }

    public override async Task<ManagedServerResponse> OpenManagedResourceDirectoryAsync(string serverId, int resourceId,
        string libraryEpoch, CancellationToken ct = default)
    {
        if (resourceId <= 0 || string.IsNullOrWhiteSpace(libraryEpoch))
            throw new ArgumentException("A resource and source library identity are required.");
        var (relay, _) = await EnsureRelayAsync(serverId, ct);
        if (relay?.Services == null) throw new InvalidOperationException("This server is no longer managed.");
        var identity = await relay.Identity!.EnsureAsync(ct);
        if (identity?.IsConfirmed != true) throw new InvalidOperationException("The source device could not be verified.");
        using var output = new MemoryStream();
        var context = new DefaultHttpContext {RequestServices = relay.Services, RequestAborted = ct};
        context.Response.Body = output;
        context.Request.QueryString = QueryString.Create(new Dictionary<string, string?>
            {["id"] = resourceId.ToString(), ["managedLibraryEpoch"] = libraryEpoch});
        await relay.Services.GetServices<IUserMachineHandler>().OfType<OpenResourceDirectoryHandler>().Single()
            .HandleAsync(context, new Dictionary<string, string>());
        return new ManagedServerResponse(context.Response.StatusCode, "application/json",
            System.Text.Encoding.UTF8.GetString(output.ToArray()));
    }

    public override async Task<ManagedServerResponse> BatchPlayManagedResourcesAsync(string serverId, int[] resourceIds,
        string libraryEpoch, string? playerKey, int fileSelectionMode, CancellationToken ct = default)
    {
        if (resourceIds is not {Length: > 0 and <= 100} || resourceIds.Any(id => id <= 0) || string.IsNullOrWhiteSpace(libraryEpoch))
            throw new ArgumentException("A bounded resource selection and source library identity are required.");
        var (relay, _) = await EnsureRelayAsync(serverId, ct);
        if (relay?.Services == null) throw new InvalidOperationException("This server is no longer managed.");
        var identity = await relay.Identity!.EnsureAsync(ct);
        if (identity?.IsConfirmed != true) throw new InvalidOperationException("The source device could not be verified.");
        await using var scope = relay.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ManagedBatchPlayContext>().LibraryEpoch = libraryEpoch;
        using var output = new MemoryStream();
        using var input = new MemoryStream(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new {resourceIds, playerKey, fileSelectionMode}));
        var context = new DefaultHttpContext {RequestServices = scope.ServiceProvider, RequestAborted = ct};
        context.Request.Body = input;
        context.Response.Body = output;
        var route = playerKey == null ? "POST /player/batch-play/candidates" : "POST /player/batch-play";
        await scope.ServiceProvider.GetServices<IUserMachineHandler>().Single(handler => handler.RouteKey == route)
            .HandleAsync(context, new Dictionary<string, string>());
        return new ManagedServerResponse(context.Response.StatusCode, "application/json", System.Text.Encoding.UTF8.GetString(output.ToArray()));
    }

    public override async Task<ManagedServerOpenView?> OpenAsync(string serverId, string? path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var (relay, _) = await EnsureRelayAsync(serverId, ct);

        if (relay == null)
        {
            return null;
        }

        // Before the window goes there, ask the address who answers — unless the relay asked
        // recently and nothing since has made it doubt the answer. On a fresh relay this is
        // also its first chance to learn how far this server's clock is from ours: every
        // request it signs is checked against a five-minute window, so a machine with a
        // wrong clock would otherwise show a revoked-looking server until somebody probed it.
        //
        // The URL is handed out whatever the answer: the relay refuses to forward on its own,
        // and a window sent there is shown why, with the way back. This only means the
        // window's first request need not wait for the question.
        await VerifyBeforeOpeningAsync(relay, ct);

        ct.ThrowIfCancellationRequested();

        // Forgotten while this was starting its relay: that relay is being stopped, and a
        // ticket for it would send the window to a port nobody listens on.
        if (_store.Find(serverId) == null)
        {
            return null;
        }

        return new ManagedServerOpenView(RelayUrls.BuildRelayUrl(relay.Port, path, _tokens.Mint(relay.Port)));
    }

    #endregion

    #region IMainViewSwitcher

    public IReadOnlyList<MainViewTarget> ListTargets() =>
    [
        new MainViewTarget(MainViewTarget.LocalId, LocalName, true),
        .._store.Read().Servers.Select(s => new MainViewTarget(s.ServerId,
            string.IsNullOrWhiteSpace(s.ServerName) ? s.BaseAddress : s.ServerName, false))
    ];

    public Task<string?> ResolveUrlAsync(string targetId, CancellationToken ct = default) =>
        ResolveUrlAsync(targetId, null, ct);

    public async Task<string?> ResolveUrlAsync(string targetId, string? path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.Equals(targetId, MainViewTarget.LocalId, StringComparison.Ordinal))
        {
            // No ticket: this device's own server takes loopback navigations as they are.
            return _localOrigin.BuildUrl(path);
        }

        return (await OpenAsync(targetId, path, ct))?.Url;
    }

    #endregion

    private async Task VerifyBeforeOpeningAsync(ManagedServerRelay relay, CancellationToken ct)
    {
        var identity = relay.Identity;

        if (identity == null)
        {
            return;
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        budget.CancelAfter(_options.ProbeBudget);

        try
        {
            await identity.EnsureAsync(budget.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Out of budget. The question goes on in the relay, and the window's first
            // request waits for its answer instead.
        }
    }
    protected override void Share(UpstreamIdentityCheck check)
    {
        if (_relays.TryGetValue(check.ServerId, out var relay))
        {
            relay.Identity?.Record(check);
        }
    }
    #region Relays

    private async Task<(ManagedServerRelay? Relay, bool Started)> EnsureRelayAsync(string serverId,
        CancellationToken ct)
    {
        await _relayGate.WaitAsync(ct);
        try
        {
            if (_lifetime.IsCancellationRequested)
            {
                return (null, false);
            }

            if (_relays.TryGetValue(serverId, out var running))
            {
                return (running, false);
            }

            var entry = _store.Find(serverId);

            if (entry == null)
            {
                return (null, false);
            }

            var refused = new HashSet<int>();

            for (var attempt = 0; attempt < MaxBindAttempts; attempt++)
            {
                var port = ChooseRelayPort(entry, refused);
                var relay = new ManagedServerRelay(serverId, port, Dependencies(serverId));

                try
                {
                    await relay.StartAsync(ct);
                }
                catch (IOException e)
                {
                    // Taken between the check and the bind. Rare, and the next port will do.
                    _logger.LogWarning(e, "Could not bind the relay for {ServerId} to port {Port}", serverId, port);
                    refused.Add(port);
                    await relay.DisposeAsync();
                    continue;
                }

                _relays[serverId] = relay;

                var retired = _store.Read().RetiredRelayPorts;

                if (entry.RelayPort != port || retired?.Any(r =>
                        string.Equals(r.Key, serverId, StringComparison.Ordinal) || r.Value == port) == true)
                {
                    // Whatever happens to the caller now: the relay is up on this port, and
                    // the next launch has to find it here too, or the server's origin — and
                    // the browser storage keyed to it — moves.
                    await _store.MutateAsync(data =>
                    {
                        if (Find(data, serverId) is { } target)
                        {
                            target.RelayPort = port;
                        }

                        // The origin is this server's now: no longer held for it, and no longer
                        // held for whichever server had it before, if it came to that.
                        if (data.RetiredRelayPorts != null)
                        {
                            foreach (var key in data.RetiredRelayPorts
                                         .Where(r => string.Equals(r.Key, serverId, StringComparison.Ordinal) ||
                                                     r.Value == port)
                                         .Select(r => r.Key).ToList())
                            {
                                data.RetiredRelayPorts.Remove(key);
                            }

                            if (data.RetiredRelayPorts.Count == 0)
                            {
                                data.RetiredRelayPorts = null;
                            }
                        }
                    }, CancellationToken.None);
                }

                _logger.LogInformation("Relay for {ServerName} ({ServerId}) listening on 127.0.0.1:{Port}",
                    entry.ServerName, serverId, port);

                return (relay, true);
            }

            throw new IOException($"Could not start a relay for {serverId}: no port could be bound.");
        }
        finally
        {
            _relayGate.Release();
        }
    }

    protected override async Task StopRelayAsync(string serverId)
    {
        await _relayGate.WaitAsync(CancellationToken.None);
        try
        {
            if (_relays.TryRemove(serverId, out var relay))
            {
                await relay.DisposeAsync();
            }
        }
        finally
        {
            _relayGate.Release();
        }
    }

    /// <summary>
    /// This server's port from last time if it is still free, otherwise the first free one
    /// from <see cref="RemoteConsoleOptions.FirstRelayPort"/> that no other server claims.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ports other servers own are skipped even while their relays are not running: a relay
    /// starts on first open, and handing its port to another server in the meantime would
    /// swap the two servers' browser storage the moment both were opened.
    /// </para>
    /// <para>
    /// The same goes for servers no longer managed: the browser still holds their storage
    /// under their origin, and another server's page there could read it. Their ports go
    /// to a new server only once nothing else in the range is left, and back to the same
    /// server when it is paired again.
    /// </para>
    /// </remarks>
    private int ChooseRelayPort(ClientServerConnection entry, IReadOnlySet<int> refused)
    {
        var taken = new HashSet<int>(ServicePorts());
        taken.UnionWith(refused);

        foreach (var relay in _relays.Values)
        {
            taken.Add(relay.Port);
        }

        var data = _store.Read();

        foreach (var server in data.Servers)
        {
            if (!string.Equals(server.ServerId, entry.ServerId, StringComparison.Ordinal) &&
                server.RelayPort is { } owned)
            {
                taken.Add(owned);
            }
        }

        int? ownRetired = null;
        var othersRetired = new HashSet<int>();

        foreach (var (serverId, retiredPort) in data.RetiredRelayPorts ?? new Dictionary<string, int>())
        {
            if (string.Equals(serverId, entry.ServerId, StringComparison.Ordinal))
            {
                ownRetired = retiredPort;
            }
            else
            {
                othersRetired.Add(retiredPort);
            }
        }

        if ((entry.RelayPort ?? ownRetired) is { } previous && !taken.Contains(previous) &&
            !othersRetired.Contains(previous) && IsFree(previous))
        {
            return previous;
        }

        var last = Math.Min(IPEndPoint.MaxPort, _consoleOptions.FirstRelayPort + _consoleOptions.RelayPortRange - 1);

        // A port no server has had before, and only then one a server no longer managed had:
        // better another server's leftover storage than no relay at all.
        foreach (var avoidRetired in new[] {true, false})
        {
            for (var port = _consoleOptions.FirstRelayPort; port <= last; port++)
            {
                if (!taken.Contains(port) && !(avoidRetired && othersRetired.Contains(port)) && IsFree(port))
                {
                    return port;
                }
            }
        }

        throw new IOException($"No free loopback port between {_consoleOptions.FirstRelayPort} and {last}.");
    }

    /// <summary>
    /// Whether nothing else on this machine uses <paramref name="port"/> for loopback traffic.
    /// </summary>
    /// <remarks>
    /// A bind test alone is not enough. A program listening on every interface (0.0.0.0)
    /// does not stop a later bind to 127.0.0.1 on the same port — .NET binds with address
    /// reuse on Unix, and BSD-derived systems then let the more specific address win — and
    /// the relay would silently take that program's local traffic from it. So the port also
    /// has to refuse a connection. On loopback that answer is immediate either way; a
    /// connection that neither completes nor is refused within the short wait counts as
    /// free rather than holding up every port behind it.
    /// </remarks>
    private static bool IsFree(int port) => CanBind(port) && !Accepts(port);

    private static bool CanBind(int port)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static bool Accepts(int port)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        try
        {
            var connect = socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port));

            return connect.Wait(TimeSpan.FromMilliseconds(250)) && socket.Connected;
        }
        catch (AggregateException e) when (e.InnerException is SocketException)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private ManagedServerRelayDependencies Dependencies(string serverId) => new(
        _store,
        _tokens,
        ClockFor(serverId),
        _self,
        new ConsoleRelayContext(serverId, AppService.CoreVersion.ToString(), this)
        {
            AnalyticsContext = () => _services.GetService<Bakabase.Abstractions.Components.Telemetry.IDesktopAnalyticsContextProvider>()?.Read()
                ?? new Bakabase.Abstractions.Components.Telemetry.DesktopAnalyticsContext(null, AppService.CoreVersion.ToString(), false)
        },
        _loggerFactory,
        _services.GetService<AppService>(),
        _services.GetService<IGuiAdapter>(),
        this,
        new UpstreamIdentityPolicy(_options.IdentityCheckInterval, _options.IdentityRetryInterval,
            _options.IdentityCheckTimeout, _options.IdentityConnectionWindow),
        _options.Connector,
        _services.GetService<IDevicePlaybackPreferencesStore>(),
        _services.GetService<IServiceProviderIsService>()?.IsService(typeof(ISourceResourceProfilePlaybackService)) == true
            ? _services.GetRequiredService<IServiceScopeFactory>() : null);

    /// <summary>This app's own server's ports, as it reports them.</summary>
    private IEnumerable<int> ServicePorts()
    {
        var context = _services.GetService<AppContext>();

        if (context == null)
        {
            yield break;
        }

        foreach (var address in context.ListeningAddresses.Concat(context.ApiEndpoints)
                     .Append(context.ApiEndpoint))
        {
            if (!string.IsNullOrEmpty(address) &&
                Uri.TryCreate(address.Replace("0.0.0.0", "localhost", StringComparison.Ordinal), UriKind.Absolute,
                    out var uri) && uri.Port > 0)
            {
                yield return uri.Port;
            }
        }
    }

    /// <summary>Every port that reaches this app: its server's and every relay's, running or reserved.</summary>
    protected override IEnumerable<int> OwnPorts()
    {
        var ports = new HashSet<int>(ServicePorts());

        foreach (var relay in _relays.Values)
        {
            ports.Add(relay.Port);
        }

        foreach (var server in _store.Read().Servers)
        {
            if (server.RelayPort is { } port)
            {
                ports.Add(port);
            }
        }

        return ports;
    }

    #endregion

}
