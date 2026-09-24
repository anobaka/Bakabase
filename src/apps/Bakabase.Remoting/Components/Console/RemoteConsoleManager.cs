using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Bakabase.Abstractions.Components.Gui;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Bakabase.Remoting.Abstractions.Models;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Remoting.Components.Forwarding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AppContext = Bakabase.Infrastructures.Components.App.AppContext;

namespace Bakabase.Remoting.Components.Console;

/// <summary>
/// The desktop app's side of managing other servers: pairing with them, knowing how they
/// are, and running one relay per server so its own UI can be shown in this window.
/// </summary>
/// <remarks>
/// <para>
/// Management is legacy paired-device access — the other server's paired-device key,
/// <c>Bakabase-Device</c> signing, everything its own UI can do — the same access the
/// removed thin client had, from the app itself and for any number of servers. Each
/// server gets its own relay, own
/// container and own stable loopback port (<see cref="ManagedServerRelay"/>); what they
/// share is only this device: its data directory, its windows, its players and its name.
/// </para>
/// <para>
/// It never touches a server's settings on its own. A server open to anyone on its network
/// (<see cref="RemoteAccessMode.Unrestricted"/>) is reported, so the UI can warn; changing
/// that is the user's decision, made on that server.
/// </para>
/// <para>
/// Keys stay in <see cref="ManagedServerStore"/>. Nothing this class returns, logs or hands
/// a relay's endpoints carries one; each relay reads its own server's through a one-entry
/// view and signs with it, and that is the only place a key is ever used.
/// </para>
/// <para>
/// A server is its install identity, not its address. Every relay asks the address who
/// answers before it forwards anything (<see cref="UpstreamIdentity"/>, with this class as
/// the verifier), and so does every probe; an address that answers as another install, or
/// as this device itself, is reported as <see cref="ManagedServerState.WrongServer"/> and
/// gets nothing — it is never paired with, renamed after or trusted in the server's place.
/// </para>
/// </remarks>
public sealed class RemoteConsoleManager : IManagedServerService, IMainViewSwitcher, IRemoteConsoleNavigator,
    IUpstreamIdentityVerifier, IHostedService, IAsyncDisposable
{
    private const int MaxBindAttempts = 4;

    /// <summary>How long a filed request is polled at most, whatever the server said about its expiry.</summary>
    private static readonly TimeSpan MaxRequestLifetime = TimeSpan.FromHours(1);

    private readonly RemoteConsoleOptions _options;
    private readonly ManagedServerStore _store;
    private readonly IRemoteAccessService _remoteAccess;
    private readonly RemoteConsoleLocalOrigin _localOrigin;
    private readonly RelayNavigationTokens _tokens;
    private readonly LegacyClientConnectionSource _legacy;
    private readonly IServerDiscovery _discovery;
    private readonly IServiceProvider _services;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly HttpClient _http;
    private readonly ClientSelfAddress _self;

    private readonly SemaphoreSlim _relayGate = new(1, 1);
    private readonly ConcurrentDictionary<string, ManagedServerRelay> _relays = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ServerClock> _clocks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ProbeSnapshot> _probes = new(StringComparer.Ordinal);

    /// <summary>
    /// When the latest answer about who serves each server's address was asked, so a slower
    /// answer to an earlier question — a probe and a relay asking at once — never overwrites it.
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTimeOffset> _identityAskedAt = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PendingRequest> _requests = new(StringComparer.Ordinal);

    /// <summary>
    /// The latest answer each server gave as itself that the store does not reflect yet, and
    /// the background write taking care of it — see <see cref="NoteAnswered"/>. Both by server
    /// id, both under <see cref="_answeredGate"/>.
    /// </summary>
    private readonly Dictionary<string, AnsweredNote> _unwritten = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task> _answeredWriters = new(StringComparer.Ordinal);
    private readonly Lock _answeredGate = new();

    private readonly CancellationTokenSource _lifetime = new();
    private Task _startup = Task.CompletedTask;
    private int _stopped;

    public RemoteConsoleManager(
        RemoteConsoleOptions options,
        ManagedServerStore store,
        IRemoteAccessService remoteAccess,
        RemoteConsoleLocalOrigin localOrigin,
        RelayNavigationTokens tokens,
        LegacyClientConnectionSource legacy,
        RemoteConsoleSwitcher switcher,
        IServerDiscovery discovery,
        IServiceProvider services,
        ILoggerFactory loggerFactory)
    {
        _options = options;
        _store = store;
        _remoteAccess = remoteAccess;
        _localOrigin = localOrigin;
        _tokens = tokens;
        _legacy = legacy;
        _discovery = discovery;
        _services = services;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<RemoteConsoleManager>();
        _self = new ClientSelfAddress(OwnPorts);

        // Its own client rather than the app's: proxies are bypassed, as the relay's are —
        // a system proxy has no route to a server on the LAN — and a redirect is an answer
        // to report, not one to follow.
        _http = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        // Last, once everything ListTargets reads is in place: from here on the shell's tray
        // lists the managed servers too, answered from the snapshot the store already holds.
        switcher.Attach(this);
    }

    /// <summary>The startup work — the one-time import — for a caller that has to wait for it.</summary>
    public Task Startup => _startup;

    /// <summary>The port each running relay listens on, by server id.</summary>
    public IReadOnlyDictionary<string, int> RunningRelays =>
        _relays.ToDictionary(r => r.Key, r => r.Value.Port, StringComparer.Ordinal);

    /// <summary>A running relay's own container, for checking how it was composed. Null when it is not running.</summary>
    internal IServiceProvider? RelayServices(string serverId) =>
        _relays.TryGetValue(serverId, out var relay) ? relay.Services : null;

    /// <summary>
    /// The name this device's own server gives itself, so it reads the same in the switcher
    /// as it does to every other device.
    /// </summary>
    public string LocalName => RemoteConsoleSwitcher.LocalName;

    public string? LocalOrigin => _localOrigin.Origin;

    #region Hosting

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _startup = Task.Run(RunStartupAsync, CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task RunStartupAsync()
    {
        try
        {
            if (!_options.ImportLegacyClientOnStart || _store.Read().LegacyClientImportedAt != null)
            {
                return;
            }

            var result = await ImportFromLegacyClientAsync(_lifetime.Token);

            if (result.Found)
            {
                _logger.LogInformation(
                    "Brought over the removed thin client's pairings: {Imported} imported, {Skipped} already here or unusable",
                    result.Imported, result.Skipped);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            // Never worth failing the app's start over: the import can be re-run from the
            // devices page, and pairing again is always possible.
            _logger.LogWarning(e, "Could not bring over the removed thin client's pairings");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1)
        {
            return;
        }

        await _lifetime.CancelAsync();

        foreach (var request in _requests.Values)
        {
            request.Cancel();
        }

        // Cancelled with the lifetime; waited for so none of them writes the store after the
        // app has stopped with it.
        Task[] writers;

        lock (_answeredGate)
        {
            writers = _answeredWriters.Values.ToArray();
        }

        await Task.WhenAll(writers);

        await _relayGate.WaitAsync(CancellationToken.None);
        try
        {
            // All at once rather than one after another: each relay may hold a hub
            // connection or a video stream open and so take its whole stop timeout, and the
            // app's exit waits on this.
            await Task.WhenAll(_relays.Values.Select(relay => relay.DisposeAsync().AsTask()));

            _relays.Clear();
        }
        finally
        {
            _relayGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _http.Dispose();
    }

    #endregion

    #region IManagedServerService

    public async Task<ManagedServersView> GetAsync(bool probe, CancellationToken ct = default)
    {
        if (probe)
        {
            await Task.WhenAll(_store.Read().Servers.Select(s => ProbeServerAsync(s, ct)));
        }

        return new ManagedServersView(true, _store.Read().Servers.Select(ToView).ToList(), RequestViews());
    }

    public async Task<ManagedServerProbeView> ProbeAsync(string address, CancellationToken ct = default)
    {
        var handshake = await HandshakeAsync(ServerConnector.Normalize(address), new ServerClock(), ct);
        var outcome = await ClassifyAsync(handshake);
        var server = handshake.Server;

        return new ManagedServerProbeView(
            outcome,
            server?.Id,
            server?.Name,
            server?.AppVersion,
            server?.Mode,
            server?.PairingSupported ?? false,
            server != null && _store.Find(server.Id) != null,
            handshake.Detail);
    }

    public async Task<ManagedServerDiscoveryView> DiscoverAsync(CancellationToken ct = default)
    {
        var ownId = await _remoteAccess.GetOrCreateServerIdAsync();
        var found = await _discovery.DiscoverAsync(_options.DiscoveryTimeout, ct);
        var managed = _store.Read().Servers.Select(s => s.ServerId).ToHashSet(StringComparer.Ordinal);

        // This device, however it answered: from loopback — its own server, right here — or
        // under its own identity from one of its LAN addresses. An id that answered from
        // loopback is this machine's on every other interface too. Managing yourself is what
        // the app does without a relay, and pairing with yourself is refused anyway.
        var local = found.Where(s => s.IsThisMachine).Select(s => s.ServerId).ToHashSet(StringComparer.Ordinal);
        local.Add(ownId);

        var servers = found
            .Where(s => !string.IsNullOrWhiteSpace(s.ServerId) && !local.Contains(s.ServerId))
            // One row per install: a server answering on two interfaces, or on both the
            // probe and mDNS, is still one server to pair with.
            .DistinctBy(s => s.ServerId, StringComparer.Ordinal)
            .Select(s => new ManagedServerCandidateView(s.ServerId, s.ServerName, s.BaseAddress, s.AppVersion,
                managed.Contains(s.ServerId)))
            .ToList();

        return new ManagedServerDiscoveryView(servers);
    }

    public async Task<ManagedServerPairingView> PairAsync(string address, string? code, CancellationToken ct = default)
    {
        var normalized = ServerConnector.Normalize(address);
        var clock = new ServerClock();
        var handshake = await HandshakeAsync(normalized, clock, ct);
        var outcome = await ClassifyAsync(handshake);

        if (outcome != ManagedServerOutcome.Ok)
        {
            return new ManagedServerPairingView(outcome, null, null, null, null, handshake.Detail);
        }

        var server = handshake.Server!;
        var pairing = new ClientPairingService(_http, _store);

        if (!string.IsNullOrWhiteSpace(code))
        {
            var result = await pairing.PairWithCodeAsync(normalized, code.Trim(), ct);

            if (!result.Succeeded)
            {
                return new ManagedServerPairingView(Map(result.Outcome), null, null, null, null, result.Detail);
            }

            await SaveAsync(server, normalized, result.Credentials!, clock, ct);

            return new ManagedServerPairingView(ManagedServerOutcome.Ok, server.Id, server.Name, null, null, null);
        }

        var request = await pairing.RequestPairingAsync(normalized, ct);

        if (request.Ticket == null)
        {
            return new ManagedServerPairingView(Map(request.Outcome), null, null, null, null, request.Detail);
        }

        StartClaiming(request.Ticket, normalized, server, clock);

        return new ManagedServerPairingView(ManagedServerOutcome.AwaitingApproval, server.Id, server.Name,
            request.Ticket.RequestId, request.Ticket.ExpiresAt, null);
    }

    public Task<bool> CancelRequestAsync(string requestId, CancellationToken ct = default)
    {
        if (!_requests.TryRemove(requestId, out var request))
        {
            return Task.FromResult(false);
        }

        request.Cancel();
        return Task.FromResult(true);
    }

    public async Task<bool> ForgetAsync(string serverId, CancellationToken ct = default)
    {
        var entry = _store.Find(serverId);

        if (entry == null)
        {
            await StopRelayAsync(serverId);
            return false;
        }

        // Forgotten here first, then asked there. Asking is best effort and can take a couple
        // of round trips — the address has to answer as the server before it is sent anything
        // signed — and the server must stop being openable the moment the user says so: an
        // open finishing meanwhile would hand the window a ticket to a relay about to stop.
        // The key needed to ask is in hand, in the entry read above.
        await _store.MutateAsync(data =>
        {
            // Its origin outlives it: the browser keeps that server's storage under it. See
            // ClientConnectionData.RetiredRelayPorts.
            if (Find(data, serverId)?.RelayPort is { } port)
            {
                (data.RetiredRelayPorts ??= new Dictionary<string, int>(StringComparer.Ordinal))[serverId] = port;
            }

            data.Servers.RemoveAll(s => string.Equals(s.ServerId, serverId, StringComparison.Ordinal));
        }, ct);

        await StopRelayAsync(serverId);

        _probes.TryRemove(serverId, out _);

        await RevokeSelfAsync(entry, ct);

        // After asking, which reads the server's clock offset to sign with.
        _clocks.TryRemove(serverId, out _);
        _identityAskedAt.TryRemove(serverId, out _);

        _logger.LogInformation("Stopped managing {ServerName} ({ServerId})", entry.ServerName, serverId);

        return true;
    }

    public async Task<bool> SetPathMappingsAsync(string serverId, IReadOnlyList<ManagedServerPathMapping> mappings,
        CancellationToken ct = default) =>
        await _store.MutateAsync(data =>
        {
            var entry = Find(data, serverId);

            if (entry == null)
            {
                return false;
            }

            // Replaced as a whole: the page edits a table, and a merge would keep a row the
            // user deleted.
            entry.PathMappings = mappings
                .Where(m => !string.IsNullOrWhiteSpace(m.ServerPath) && !string.IsNullOrWhiteSpace(m.LocalPath))
                .Select(m => new ClientPathMapping {ServerPath = m.ServerPath.Trim(), LocalPath = m.LocalPath.Trim()})
                .ToList();

            return true;
        }, ct);

    public async Task<ManagedServerOpenView?> OpenAsync(string serverId, string? path, CancellationToken ct = default)
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

    public async Task<ManagedServerImportView> ImportFromLegacyClientAsync(CancellationToken ct = default)
    {
        var legacy = _legacy.Read();

        if (legacy == null || legacy.Servers.Count == 0)
        {
            return new ManagedServerImportView(false, 0, 0);
        }

        var ownId = await _remoteAccess.GetOrCreateServerIdAsync();
        var now = DateTime.UtcNow;

        var (imported, skipped) = await _store.MutateAsync(data =>
        {
            var added = 0;
            var passed = 0;

            foreach (var server in legacy.Servers)
            {
                if (string.IsNullOrWhiteSpace(server.ServerId) ||
                    string.IsNullOrWhiteSpace(server.BaseAddress) ||
                    string.IsNullOrWhiteSpace(server.DeviceId) ||
                    string.IsNullOrWhiteSpace(server.DeviceKey) ||
                    // The thin client pointed at this very install: managing yourself is
                    // what the app does without a relay.
                    string.Equals(server.ServerId, ownId, StringComparison.Ordinal) ||
                    // Never over a pairing made here: it is at least as new, and it is the
                    // one the user chose in this app.
                    Find(data, server.ServerId) != null)
                {
                    passed++;
                    continue;
                }

                data.Servers.Add(new ClientServerConnection
                {
                    ServerId = server.ServerId,
                    ServerName = server.ServerName,
                    BaseAddress = ServerConnector.Normalize(server.BaseAddress),
                    DeviceId = server.DeviceId,
                    DeviceKey = server.DeviceKey,
                    PairedAt = server.PairedAt,
                    LastConnectedAt = server.LastConnectedAt,
                    PathMappings = (server.PathMappings ?? [])
                        .Where(m => !string.IsNullOrWhiteSpace(m.ServerPath) && !string.IsNullOrWhiteSpace(m.LocalPath))
                        .Select(m => new ClientPathMapping {ServerPath = m.ServerPath, LocalPath = m.LocalPath})
                        .ToList(),
                    ImportedFromLegacyClient = true
                });

                added++;
            }

            // The name this device paired under before, so the servers it is already
            // known to keep seeing the same name when it pairs somewhere new.
            if (string.IsNullOrWhiteSpace(data.DeviceName) && !string.IsNullOrWhiteSpace(legacy.DeviceName))
            {
                data.DeviceName = legacy.DeviceName;
            }

            data.LegacyClientImportedAt = now;

            return (added, passed);
        }, ct);

        return new ManagedServerImportView(true, imported, skipped);
    }

    #endregion

    #region IMainViewSwitcher

    public IReadOnlyList<MainViewTarget> ListTargets() =>
    [
        new MainViewTarget(MainViewTarget.LocalId, LocalName, true),
        .._store.Read().Servers.Select(s => new MainViewTarget(s.ServerId,
            string.IsNullOrWhiteSpace(s.ServerName) ? s.BaseAddress : s.ServerName, false))
    ];

    /// <remarks>
    /// The same snapshot <see cref="GetAsync"/> reports, as the last probe left it — or the
    /// last pairing, which is the server answering too.
    /// </remarks>
    public ManagedServerState LastKnownState(string serverId) =>
        _probes.TryGetValue(serverId, out var probe) ? probe.State : ManagedServerState.Unknown;

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

    #region Pairing

    private async Task<ServerHandshakeResult> HandshakeAsync(string address, ServerClock clock, CancellationToken ct)
    {
        try
        {
            return await new ServerConnector(_http, clock, _self).HandshakeAsync(address, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return ServerHandshakeResult.Failed(ServerHandshakeOutcome.Unreachable, e.Message);
        }
    }

    /// <summary>
    /// What a handshake means for managing, including the one case the handshake itself
    /// cannot see: an address that reached this device through a door other than its own
    /// ports — a LAN address of this machine, a name that resolves here.
    /// </summary>
    private async Task<ManagedServerOutcome> ClassifyAsync(ServerHandshakeResult handshake)
    {
        if (handshake.Server != null &&
            string.Equals(handshake.Server.Id, await _remoteAccess.GetOrCreateServerIdAsync(), StringComparison.Ordinal))
        {
            return ManagedServerOutcome.ThisDevice;
        }

        return handshake.Outcome switch
        {
            ServerHandshakeOutcome.Ok => ManagedServerOutcome.Ok,
            ServerHandshakeOutcome.Unreachable => ManagedServerOutcome.Unreachable,
            ServerHandshakeOutcome.NotBakabase => ManagedServerOutcome.NotBakabase,
            ServerHandshakeOutcome.ClientTooOld => ManagedServerOutcome.ThisAppTooOld,
            ServerHandshakeOutcome.ServerTooOld => ManagedServerOutcome.ServerTooOld,
            ServerHandshakeOutcome.RemoteAccessDisabled => ManagedServerOutcome.RemoteAccessDisabled,
            ServerHandshakeOutcome.SelfAddress => ManagedServerOutcome.ThisDevice,
            _ => ManagedServerOutcome.NotBakabase
        };
    }

    private static ManagedServerOutcome Map(ClientPairingOutcome outcome) => outcome switch
    {
        ClientPairingOutcome.Paired => ManagedServerOutcome.Ok,
        ClientPairingOutcome.Unreachable => ManagedServerOutcome.Unreachable,
        ClientPairingOutcome.CodeRejected => ManagedServerOutcome.CodeRejected,
        ClientPairingOutcome.AwaitingApproval => ManagedServerOutcome.AwaitingApproval,
        ClientPairingOutcome.RequestRejected => ManagedServerOutcome.RequestRejected,
        ClientPairingOutcome.TooManyAttempts => ManagedServerOutcome.TooManyAttempts,
        _ => ManagedServerOutcome.PairingUnsupported
    };

    /// <summary>
    /// Records credentials against the identity the handshake reported, replacing any
    /// earlier pairing with the same server but keeping what the user set up for it here.
    /// </summary>
    private async Task SaveAsync(ServerInfo server, string address, ClientCredentials credentials, ServerClock clock,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        await _store.MutateAsync(data =>
        {
            var existing = Find(data, server.Id);

            data.Servers.RemoveAll(s => string.Equals(s.ServerId, server.Id, StringComparison.Ordinal));
            data.Servers.Insert(0, new ClientServerConnection
            {
                ServerId = server.Id,
                ServerName = server.Name,
                BaseAddress = ServerConnector.Normalize(address),
                DeviceId = credentials.DeviceId,
                DeviceKey = credentials.Key,
                PairedAt = now,
                LastConnectedAt = now,
                // A re-pairing is the same server: its libraries are where they were, and
                // its relay keeps its origin — and with it the browser's storage.
                PathMappings = existing?.PathMappings ?? [],
                RelayPort = existing?.RelayPort
            });
        }, ct);

        _clocks[server.Id] = clock;
        _identityAskedAt[server.Id] = DateTimeOffset.UtcNow;
        _probes[server.Id] = new ProbeSnapshot(ManagedServerState.Online, server.Mode, server.AppVersion);

        _logger.LogInformation("Now managing {ServerName} ({ServerId}) at {Address}", server.Name, server.Id,
            ServerConnector.Normalize(address));
    }

    private void StartClaiming(ClientPairingTicket ticket, string address, ServerInfo server, ServerClock clock)
    {
        var request = new PendingRequest(ticket.RequestId, address, server.Name, ticket.ExpiresAt,
            CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token));

        _requests[ticket.RequestId] = request;

        _ = Task.Run(() => ClaimUntilAnsweredAsync(request, server, clock), CancellationToken.None);
    }

    /// <summary>
    /// Collects the answer to a filed request. Polled rather than pushed: the approval
    /// happens on the other device, and this one has no connection to it yet — that is
    /// what it is asking for.
    /// </summary>
    private async Task ClaimUntilAnsweredAsync(PendingRequest request, ServerInfo server, ServerClock clock)
    {
        var ct = request.Token;
        var pairing = new ClientPairingService(_http, _store);
        var giveUpAt = DateTime.UtcNow + MaxRequestLifetime;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(_options.ClaimPollInterval, ct);

                // The expiry is the server's own clock; compare it against the best
                // estimate of that clock rather than ours.
                if (clock.Now >= request.ExpiresAt || DateTime.UtcNow >= giveUpAt)
                {
                    request.Finish(ManagedServerOutcome.RequestRejected);
                    return;
                }

                ClientPairingResult result;
                try
                {
                    result = await pairing.ClaimAsync(request.Address, request.RequestId, ct);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _logger.LogDebug(e, "Asking {Address} about request {RequestId} failed", request.Address,
                        request.RequestId);
                    request.Report(ManagedServerOutcome.Unreachable);
                    continue;
                }

                switch (result.Outcome)
                {
                    case ClientPairingOutcome.Paired:
                        await CompleteClaimAsync(request, server, clock, result.Credentials!, ct);
                        return;
                    case ClientPairingOutcome.AwaitingApproval:
                    case ClientPairingOutcome.Unreachable:
                    case ClientPairingOutcome.TooManyAttempts:
                        // Still waiting, or briefly unable to ask. Neither ends the wait: the
                        // outcome says what this attempt ran into, and the request stays active.
                        request.Report(Map(result.Outcome));
                        continue;
                    default:
                        request.Finish(Map(result.Outcome));
                        return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled from the page, or the app is closing.
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Waiting on request {RequestId} at {Address} failed", request.RequestId,
                request.Address);
            request.Finish(ManagedServerOutcome.Unreachable);
        }
    }

    private async Task CompleteClaimAsync(PendingRequest request, ServerInfo server, ServerClock clock,
        ClientCredentials credentials, CancellationToken ct)
    {
        // The credentials belong to whichever install answered the claim. Ask again who
        // that is, so an address that changed hands while the request waited does not get
        // the key filed under the wrong server; if nobody answers now, the identity from
        // when the request was filed is the best there is.
        var identity = server;

        using (var budget = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            budget.CancelAfter(_options.ProbeBudget);

            try
            {
                var confirm = await HandshakeAsync(request.Address, clock, budget.Token);

                if (await ClassifyAsync(confirm) == ManagedServerOutcome.ThisDevice)
                {
                    request.Finish(ManagedServerOutcome.ThisDevice);
                    return;
                }

                if (confirm.Succeeded)
                {
                    identity = confirm.Server!;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Out of budget. The credentials are in hand; losing them over a slow
                // second question would be the worse outcome.
            }
        }

        await SaveAsync(identity, request.Address, credentials, clock, ct);

        // In this order: a listing taken between the save and the removal shows the server
        // next to a request still active, which only means one more read; one that showed the
        // request ended before the server was saved would stop the page watching for it.
        _requests.TryRemove(request.RequestId, out _);
        request.Finish(ManagedServerOutcome.Ok);
    }

    private IReadOnlyList<ManagedServerPendingRequestView> RequestViews()
    {
        var now = DateTime.UtcNow;

        foreach (var (id, request) in _requests)
        {
            if (request.FinishedAt is { } finishedAt && now - finishedAt > _options.FinishedRequestRetention)
            {
                _requests.TryRemove(id, out _);
            }
        }

        return _requests.Values
            .OrderBy(r => r.StartedAt)
            .Select(r => r.ToView())
            .ToList();
    }

    #endregion

    #region Probing

    /// <summary>
    /// Whether a managed server answers and still accepts this device, within the probe
    /// budget. Never throws.
    /// </summary>
    private async Task ProbeServerAsync(ClientServerConnection entry, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        budget.CancelAfter(_options.ProbeBudget);

        var clock = ClockFor(entry.ServerId);
        var previous = _probes.GetValueOrDefault(entry.ServerId);
        var startedAt = DateTimeOffset.UtcNow;
        ProbeSnapshot snapshot;

        try
        {
            var handshake = await HandshakeAsync(entry.BaseAddress, clock, budget.Token);
            var identity = await IdentityOfAsync(entry.ServerId, entry.BaseAddress, handshake, startedAt);
            var server = handshake.Server;

            // A running relay acts on this at once — stops forwarding, or starts again —
            // rather than on its own next question.
            Share(identity);

            if (identity.IsMismatch)
            {
                // Another install answers at that address — or this device does. The server
                // moved, or it was reinstalled or reset and is a new install now; which one
                // is for the user to find out, and neither is a reason to talk to whoever
                // answered. Nothing more is asked of it, and nothing it said is kept.
                snapshot = Mismatched(previous, identity);
            }
            else if (server == null || !handshake.Succeeded)
            {
                snapshot = new ProbeSnapshot(ManagedServerState.Offline,
                    handshake.Outcome == ServerHandshakeOutcome.RemoteAccessDisabled
                        ? RemoteAccessMode.Disabled
                        : server?.Mode ?? previous?.Mode, server?.AppVersion ?? previous?.AppVersion);
            }
            else
            {
                snapshot = new ProbeSnapshot(await ReadContextAsync(entry, clock, budget.Token), server.Mode,
                    server.AppVersion);

                if (snapshot.State == ManagedServerState.Online)
                {
                    NoteAnswered(entry, server.Name);
                }
            }
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            // Out of budget, or something between here and there failed. A listing waits
            // on this, so the answer is "not reachable right now" rather than an error.
            if (e is not (OperationCanceledException or HttpRequestException))
            {
                _logger.LogDebug(e, "Probing {ServerId} failed", entry.ServerId);
            }

            snapshot = new ProbeSnapshot(ManagedServerState.Offline, previous?.Mode, previous?.AppVersion);
        }

        if (NoteIdentityAsked(entry.ServerId, startedAt))
        {
            _probes[entry.ServerId] = snapshot;
        }
    }

    /// <summary>
    /// Notes that an answer about who serves <paramref name="serverId"/>'s address, asked at
    /// <paramref name="askedAt"/>, is being recorded. False when an answer asked later has
    /// already been recorded: that one stands.
    /// </summary>
    private bool NoteIdentityAsked(string serverId, DateTimeOffset askedAt)
    {
        while (true)
        {
            if (!_identityAskedAt.TryGetValue(serverId, out var known))
            {
                if (_identityAskedAt.TryAdd(serverId, askedAt))
                {
                    return true;
                }

                continue;
            }

            if (known > askedAt)
            {
                return false;
            }

            if (_identityAskedAt.TryUpdate(serverId, askedAt, known))
            {
                return true;
            }
        }
    }

    /// <summary>
    /// Asks the server, signed as this device, what it makes of this device.
    /// </summary>
    private async Task<ManagedServerState> ReadContextAsync(ClientServerConnection entry, ServerClock clock,
        CancellationToken ct)
    {
        if (!Uri.TryCreate(entry.BaseAddress, UriKind.Absolute, out var root))
        {
            return ManagedServerState.Offline;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(root, ClientContextEndpoint.Path));

        if (!await TrySignAsync(request, entry, clock, ct))
        {
            // A stored key that does not decode manages nothing. Pairing again is the fix,
            // which is what Revoked sends the user to do.
            return ManagedServerState.Revoked;
        }

        using var response = await _http.SendAsync(request, ct);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            var reason = response.Headers.TryGetValues("X-Bakabase-Remote-Access", out var values)
                ? values.FirstOrDefault()
                : null;

            return reason is nameof(RemoteAccessDenialReason.DeviceRevoked) or
                nameof(RemoteAccessDenialReason.Unauthenticated)
                ? ManagedServerState.Revoked
                // Switched off, or a clock too far out to sign for: not this device's
                // standing, just not usable right now.
                : ManagedServerState.Offline;
        }

        if (!response.IsSuccessStatusCode)
        {
            return ManagedServerState.Offline;
        }

        ContextPayload? payload;
        try
        {
            payload = (await JsonSerializer.DeserializeAsync<Envelope<ContextPayload>>(
                await response.Content.ReadAsStreamAsync(ct), ServerJson.Options, ct))?.Data;
        }
        catch (JsonException)
        {
            return ManagedServerState.Offline;
        }

        if (payload == null)
        {
            return ManagedServerState.Offline;
        }

        // A server on this same machine sees this device as local and does not look at
        // the key at all; that is full access, the thing managing it needs.
        return payload.Paired || payload.IsLocal ? ManagedServerState.Online : ManagedServerState.Revoked;
    }

    /// <summary>What a server said about itself when it answered as itself, and when.</summary>
    private sealed record AnsweredNote(string? Name, DateTime At);

    /// <summary>
    /// Keeps the stored name current, and notes that the server answered as itself — rarely
    /// enough not to churn the file, and never on the path of whoever heard the answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bookkeeping, not a verdict. Who answers at the address, and whether it takes this
    /// device, is decided before this is called and waits for nothing here: a store that
    /// cannot be written — a full disk, a file a scanner holds — must never turn a server
    /// that answered as itself into one nobody answers for, and with it refuse every request
    /// the relay has for the right server.
    /// </para>
    /// <para>
    /// So the write happens in the background, one writer per server holding only the latest
    /// answer. One that fails is logged and tried again every
    /// <see cref="RemoteConsoleOptions.StoreRetryInterval"/>, whether or not the server
    /// answers again meanwhile, until it lands, the server is forgotten or the app stops.
    /// Until then the store — and so every answer after this one — still reads as not written,
    /// and each of those answers simply becomes the one the writer writes.
    /// </para>
    /// </remarks>
    private void NoteAnswered(ClientServerConnection entry, string? name)
    {
        var now = DateTime.UtcNow;
        var renamed = !string.IsNullOrWhiteSpace(name) &&
                      !string.Equals(name, entry.ServerName, StringComparison.Ordinal);
        var stale = entry.LastConnectedAt is not { } last ||
                    now - last >= ActiveConnection.LastConnectedPersistenceInterval;

        if (!renamed && !stale)
        {
            return;
        }

        var serverId = entry.ServerId;

        lock (_answeredGate)
        {
            if (_lifetime.IsCancellationRequested)
            {
                return;
            }

            _unwritten[serverId] = new AnsweredNote(renamed ? name : null, now);

            if (!_answeredWriters.ContainsKey(serverId))
            {
                _answeredWriters[serverId] = Task.Run(() => WriteAnsweredAsync(serverId), CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Writes <paramref name="serverId"/>'s latest answer into the store until none is left
    /// unwritten, waiting <see cref="RemoteConsoleOptions.StoreRetryInterval"/> after a
    /// failure. Never throws.
    /// </summary>
    private async Task WriteAnsweredAsync(string serverId)
    {
        var failures = 0;

        while (NextUnwritten(serverId) is { } note)
        {
            try
            {
                // Forgotten meanwhile: nothing left to note it on, and no reason to write.
                if (_store.Find(serverId) != null)
                {
                    await _store.MutateAsync(data =>
                    {
                        var target = Find(data, serverId);

                        if (target == null)
                        {
                            return;
                        }

                        if (note.Name != null)
                        {
                            target.ServerName = note.Name;
                        }

                        target.LastConnectedAt = note.At;
                    }, _lifetime.Token);
                }

                lock (_answeredGate)
                {
                    // Written, unless a later answer came in while it was: that one is next.
                    if (_unwritten.TryGetValue(serverId, out var latest) && ReferenceEquals(latest, note))
                    {
                        _unwritten.Remove(serverId);
                    }
                }

                if (failures > 0)
                {
                    _logger.LogInformation(
                        "Noted in the managed-server store that {ServerId} answered, after {Failures} failed attempts",
                        serverId, failures);
                    failures = 0;
                }

                continue;
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                continue;
            }
            catch (Exception e)
            {
                failures++;

                if (failures == 1)
                {
                    _logger.LogWarning(e,
                        "Could not note in the managed-server store that {ServerId} answered; it is still forwarded " +
                        "to, and the write is tried again every {Interval}", serverId, _options.StoreRetryInterval);
                }
                else
                {
                    _logger.LogDebug(e, "Noting in the managed-server store that {ServerId} answered failed again ({Failures})",
                        serverId, failures);
                }
            }

            try
            {
                await Task.Delay(_options.StoreRetryInterval, _lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                // Stopping: the loop's next check ends it.
            }
        }
    }

    /// <summary>
    /// The answer still to be written for <paramref name="serverId"/>; null — and the writer
    /// taken off the books in the same step, so an answer noted a moment later starts a new
    /// one — when there is none or the app is stopping.
    /// </summary>
    private AnsweredNote? NextUnwritten(string serverId)
    {
        lock (_answeredGate)
        {
            if (!_lifetime.IsCancellationRequested && _unwritten.TryGetValue(serverId, out var note))
            {
                return note;
            }

            _answeredWriters.Remove(serverId);
            return null;
        }
    }

    /// <summary>
    /// Has the relay make sure who answers at its server's address, within the probe budget.
    /// Never throws unless <paramref name="ct"/> is cancelled.
    /// </summary>
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

    #endregion

    #region Identity

    /// <summary>
    /// Asks <paramref name="address"/> who answers there, for a relay about to forward to it,
    /// and records the answer for the listing.
    /// </summary>
    /// <remarks>
    /// The same handshake pairing and probing use, so the clock offset every signature
    /// depends on is refreshed with each answer. An answer from the right server also keeps
    /// its stored name current, as a probe does — in the background, so the verdict never
    /// depends on the store being writable (<see cref="NoteAnswered"/>); an answer from anyone
    /// else changes nothing about the entry.
    /// </remarks>
    public async Task<UpstreamIdentityCheck> VerifyAsync(string serverId, string address, CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var handshake = await HandshakeAsync(address, ClockFor(serverId), ct);
        var check = await IdentityOfAsync(serverId, address, handshake, startedAt);

        var entry = _store.Find(serverId);

        // Recorded only while it is still about the server as stored — an answer about an
        // address the entry has since left, or about a server forgotten meanwhile, says
        // nothing about either — and only when nothing asked later has been recorded first.
        if (entry != null && string.Equals(entry.BaseAddress, address, StringComparison.Ordinal) &&
            NoteIdentityAsked(serverId, startedAt))
        {
            var previous = _probes.GetValueOrDefault(serverId);

            if (check.IsMismatch)
            {
                _probes[serverId] = Mismatched(previous, check);
            }
            else if (check.IsConfirmed)
            {
                if (previous?.State == ManagedServerState.WrongServer)
                {
                    // The server is back at its address. Whether it still takes this device
                    // is the next probe's to say, or the relay's next request's.
                    _probes[serverId] = previous with {State = ManagedServerState.Unknown, AnsweredBy = null};
                }

                // Off the verdict's path: the answer stands whether or not the store can be
                // written just now.
                NoteAnswered(entry, handshake.Server!.Name);
            }
            else
            {
                _probes[serverId] = new ProbeSnapshot(ManagedServerState.Offline,
                    handshake.Outcome == ServerHandshakeOutcome.RemoteAccessDisabled
                        ? RemoteAccessMode.Disabled
                        : previous?.Mode, previous?.AppVersion);
            }
        }

        return check;
    }

    /// <summary>What a handshake with <paramref name="address"/> says about who serves <paramref name="serverId"/> there.</summary>
    /// <remarks>
    /// Only the identity counts. A server that answered with its identity but cannot be
    /// talked to otherwise — remote access switched off, a protocol too new or too old — is
    /// still the server: what it then does with a request is its own answer to give, as it
    /// always was.
    /// </remarks>
    private async Task<UpstreamIdentityCheck> IdentityOfAsync(string serverId, string address,
        ServerHandshakeResult handshake, DateTimeOffset startedAt)
    {
        if (handshake.Outcome == ServerHandshakeOutcome.SelfAddress)
        {
            // One of this app's own ports: its own server, or a relay that would hand the
            // question to some other server and come back with that one's name.
            return new UpstreamIdentityCheck(serverId, address, UpstreamIdentityVerdict.ThisDevice, null,
                LocalName, null, startedAt);
        }

        if (handshake.Server is { } server)
        {
            var verdict =
                string.Equals(server.Id, await _remoteAccess.GetOrCreateServerIdAsync(), StringComparison.Ordinal)
                    ? UpstreamIdentityVerdict.ThisDevice
                    : string.Equals(server.Id, serverId, StringComparison.Ordinal)
                        ? UpstreamIdentityVerdict.Confirmed
                        : UpstreamIdentityVerdict.WrongServer;

            return new UpstreamIdentityCheck(serverId, address, verdict, server.Id, server.Name, null, startedAt);
        }

        return new UpstreamIdentityCheck(serverId, address, UpstreamIdentityVerdict.Unconfirmed, null, null,
            handshake.Outcome switch
            {
                ServerHandshakeOutcome.Unreachable => "nothing answers there",
                ServerHandshakeOutcome.RemoteAccessDisabled => "remote access is turned off there",
                _ => "what answers there is not a Bakabase server"
            }, startedAt)
        {
            // Its gate refused before saying who it is. What the user is told to do differs:
            // turn remote access on there, not check that something is running.
            RemoteAccessDisabled = handshake.Outcome == ServerHandshakeOutcome.RemoteAccessDisabled
        };
    }

    /// <summary>Hands a relay an answer the console got itself, so it acts on it without asking again.</summary>
    private void Share(UpstreamIdentityCheck check)
    {
        if (_relays.TryGetValue(check.ServerId, out var relay))
        {
            relay.Identity?.Record(check);
        }
    }

    private static ProbeSnapshot Mismatched(ProbeSnapshot? previous, UpstreamIdentityCheck check) =>
        // The server's own mode and version as last seen, never the ones of whoever answered
        // in its place.
        new(ManagedServerState.WrongServer, previous?.Mode, previous?.AppVersion,
            new ManagedServerAnswerView(check.AnsweredById, check.AnsweredByName,
                check.Verdict == UpstreamIdentityVerdict.ThisDevice));

    #endregion

    #region Server calls

    /// <summary>
    /// Asks the server to forget this device, signed as it. Best effort: a server that is
    /// gone for good must not keep the user from dropping it here.
    /// </summary>
    private async Task RevokeSelfAsync(ClientServerConnection entry, CancellationToken ct)
    {
        if (!Uri.TryCreate(entry.BaseAddress, UriKind.Absolute, out var root))
        {
            return;
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        budget.CancelAfter(_options.ProbeBudget);

        try
        {
            // Only to the server itself. Whoever else answers at its address now has no
            // business receiving a request signed as this device — and one that takes any
            // caller as local would carry it out.
            var identity = await IdentityOfAsync(entry.ServerId, entry.BaseAddress,
                await HandshakeAsync(entry.BaseAddress, ClockFor(entry.ServerId), budget.Token),
                DateTimeOffset.UtcNow);

            if (!identity.IsConfirmed)
            {
                _logger.LogInformation(
                    "Not asking {Address} to revoke this device: it does not answer as {ServerId} ({Verdict}); " +
                    "forgetting it here anyway", entry.BaseAddress, entry.ServerId, identity.Verdict);
                return;
            }

            using var request = new HttpRequestMessage(HttpMethod.Delete,
                new Uri(root, $"/remote-access/devices/{Uri.EscapeDataString(entry.DeviceId)}"));

            if (!await TrySignAsync(request, entry, ClockFor(entry.ServerId), budget.Token))
            {
                return;
            }

            using var response = await _http.SendAsync(request, budget.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("{ServerId} did not confirm revoking this device (HTTP {Status})",
                    entry.ServerId, (int) response.StatusCode);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or HttpRequestException or IOException &&
                                  !ct.IsCancellationRequested)
        {
            _logger.LogInformation("Could not reach {ServerId} to revoke this device; forgetting it here anyway",
                entry.ServerId);
        }
    }

    private static async Task<bool> TrySignAsync(HttpRequestMessage request, ClientServerConnection entry,
        ServerClock clock, CancellationToken ct)
    {
        byte[] key;
        try
        {
            key = RemoteRequestSignature.FromBase64Url(entry.DeviceKey);
        }
        catch (FormatException)
        {
            return false;
        }

        await UpstreamRequestSigner.SignAsync(request, entry.DeviceId, key, clock.NowUnixSeconds, ct: ct);
        return true;
    }

    private ServerClock ClockFor(string serverId) => _clocks.GetOrAdd(serverId, _ => new ServerClock());

    private ManagedServerView ToView(ClientServerConnection entry)
    {
        var probe = _probes.GetValueOrDefault(entry.ServerId);

        return new ManagedServerView(
            entry.ServerId,
            entry.ServerName,
            entry.BaseAddress,
            entry.PairedAt,
            entry.LastConnectedAt,
            entry.PathMappings.Select(m => new ManagedServerPathMapping(m.ServerPath, m.LocalPath)).ToList(),
            probe?.State ?? ManagedServerState.Unknown,
            probe?.Mode,
            probe?.AppVersion,
            entry.ImportedFromLegacyClient,
            probe?.State == ManagedServerState.WrongServer ? probe.AnsweredBy : null);
    }

    #endregion

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

    private async Task StopRelayAsync(string serverId)
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

        var last = Math.Min(IPEndPoint.MaxPort, _options.FirstRelayPort + _options.RelayPortRange - 1);

        // A port no server has had before, and only then one a server no longer managed had:
        // better another server's leftover storage than no relay at all.
        foreach (var avoidRetired in new[] {true, false})
        {
            for (var port = _options.FirstRelayPort; port <= last; port++)
            {
                if (!taken.Contains(port) && !(avoidRetired && othersRetired.Contains(port)) && IsFree(port))
                {
                    return port;
                }
            }
        }

        throw new IOException($"No free loopback port between {_options.FirstRelayPort} and {last}.");
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
        new ConsoleRelayContext(serverId, AppService.CoreVersion.ToString(), this),
        _loggerFactory,
        _services.GetService<AppService>(),
        _services.GetService<IGuiAdapter>(),
        this,
        new UpstreamIdentityPolicy(_options.IdentityCheckInterval, _options.IdentityRetryInterval,
            _options.IdentityCheckTimeout, _options.IdentityConnectionWindow));

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
    private IEnumerable<int> OwnPorts()
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

    private static ClientServerConnection? Find(ClientConnectionData data, string serverId) =>
        data.Servers.FirstOrDefault(s => string.Equals(s.ServerId, serverId, StringComparison.Ordinal));

    /// <param name="AnsweredBy">Who answered at the address instead, while <paramref name="State"/> is WrongServer.</param>
    private sealed record ProbeSnapshot(ManagedServerState State, RemoteAccessMode? Mode, string? AppVersion,
        ManagedServerAnswerView? AnsweredBy = null);

    private sealed record Envelope<T>(int Code, string? Message, T? Data);

    private sealed record ContextPayload(bool IsLocal, RemoteAccessMode Mode, bool Paired);

    /// <summary>A filed request and where waiting on it has got to.</summary>
    /// <remarks>
    /// Whether the wait is over and what the last attempt said are two facts, kept apart:
    /// a claim the network dropped is reported as <see cref="ManagedServerOutcome.Unreachable"/>
    /// while the wait goes on, and a page that read the outcome alone as "ended" stopped
    /// watching a request that was still being collected. Both are read under one lock so a
    /// listing never pairs an ending with the outcome from before it.
    /// </remarks>
    private sealed class PendingRequest(
        string requestId,
        string address,
        string? serverName,
        DateTime expiresAt,
        CancellationTokenSource cancellation)
    {
        private readonly Lock _gate = new();
        private ManagedServerOutcome _outcome = ManagedServerOutcome.AwaitingApproval;
        private DateTime? _finishedAt;

        public string RequestId { get; } = requestId;
        public string Address { get; } = address;
        public string? ServerName { get; } = serverName;
        public DateTime ExpiresAt { get; } = expiresAt;
        public DateTime StartedAt { get; } = DateTime.UtcNow;
        public CancellationToken Token => cancellation.Token;

        public DateTime? FinishedAt
        {
            get
            {
                lock (_gate)
                {
                    return _finishedAt;
                }
            }
        }

        /// <summary>What the latest attempt said, while the wait goes on. Ignored once it has ended.</summary>
        public void Report(ManagedServerOutcome outcome)
        {
            lock (_gate)
            {
                if (_finishedAt == null)
                {
                    _outcome = outcome;
                }
            }
        }

        /// <summary>Stops waiting and keeps the answer for the page to show. The first ending wins.</summary>
        public void Finish(ManagedServerOutcome outcome)
        {
            lock (_gate)
            {
                if (_finishedAt != null)
                {
                    return;
                }

                _outcome = outcome;
                _finishedAt = DateTime.UtcNow;
            }
        }

        public void Cancel()
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public ManagedServerPendingRequestView ToView()
        {
            lock (_gate)
            {
                // Cancelled counts as ended even before the claim loop notices: withdrawn from
                // the page, or the app closing — either way nobody is collecting it any more.
                return new ManagedServerPendingRequestView(RequestId, Address, ServerName, ExpiresAt, _outcome,
                    _finishedAt == null && !cancellation.IsCancellationRequested);
            }
        }
    }
}
