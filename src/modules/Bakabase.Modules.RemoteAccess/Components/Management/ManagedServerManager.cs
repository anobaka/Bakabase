using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components;
using Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Bakabase.Remoting.Abstractions.Models;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Remoting.Components.Forwarding;
using Bakabase.Remoting.Components.Console;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.RemoteAccess.Components.Management;

/// <summary>Shared, identity-checked management connections for desktop and headless hosts.</summary>
public class ManagedServerManager : IManagedServerService, IUpstreamIdentityVerifier, IHostedService, IAsyncDisposable
{
    /// <summary>How long a filed request is polled at most, whatever the server said about its expiry.</summary>
    private static readonly TimeSpan MaxRequestLifetime = TimeSpan.FromHours(1);

    protected readonly ManagedServerOptions _options;
    protected readonly ManagedServerStore _store;
    private readonly IRemoteAccessService _remoteAccess;
    private readonly IServerDiscovery _discovery;
    protected readonly ILogger _logger;
    private readonly HttpClient _http;
    private readonly Lazy<ClientSelfAddress> _selfAddress;
    protected ClientSelfAddress _self => _selfAddress.Value;

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

    protected readonly CancellationTokenSource _lifetime = new();
    private int _stopped;

    public ManagedServerManager(ManagedServerOptions options, ManagedServerStore store,
        IRemoteAccessService remoteAccess, IServerDiscovery discovery, ILoggerFactory loggerFactory)
    {
        _options = options;
        _store = store;
        _remoteAccess = remoteAccess;
        _discovery = discovery;
        _logger = loggerFactory.CreateLogger<ManagedServerManager>();
        // Desktop listener services are initialized after this base constructor.
        _selfAddress = new Lazy<ClientSelfAddress>(() => new ClientSelfAddress(OwnPorts));
        _http = CreateHttpClient(_options.Connector.ConnectCallback);
    }

    public virtual string LocalName => Environment.MachineName;
    protected virtual bool CanOpenDesktopView => false;
    protected virtual IEnumerable<int> OwnPorts() => _options.LocalPorts();
    protected virtual Task StopRelayAsync(string serverId) => Task.CompletedTask;
    protected virtual Task StopAllRelaysAsync() => Task.CompletedTask;
    protected virtual void Share(UpstreamIdentityCheck check) { }

    #region Hosting

    /// <remarks>
    /// Idle until asked to pair or manage a server. Shutdown cancels requests and pending writes;
    /// a desktop adapter additionally stops its relays.
    /// </remarks>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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

        await StopAllRelaysAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _http.Dispose();
    }

    #endregion

    #region IManagedServerService

    public async Task<string> ReadResourceProfileSourceAsync(string serverId, string operation, string? json, string? libraryEpoch = null,
        CancellationToken ct = default)
    {
        var (method, path) = operation switch
        {
            "identity" => (HttpMethod.Get, "/resource-profile/source-info"),
            "properties" => (HttpMethod.Get, "/resource-profile/search/properties"),
            "filter-value" => (HttpMethod.Post, "/resource-profile/search/filter-value"),
            "search-resolve" => (HttpMethod.Post, "/resource-profile/search/resolve"),
            "evaluate" => (HttpMethod.Post, "/resource-profile/evaluate"),
            "preview" => (HttpMethod.Post, "/resource-profile/preview"),
            _ => throw new ArgumentException("Unknown resource-profile source operation.", nameof(operation))
        };
        if (json != null && System.Text.Encoding.UTF8.GetByteCount(json) > 1024 * 1024)
            throw new ArgumentException("Resource-profile search is too large.", nameof(json));
        if (libraryEpoch != null)
        {
            if (libraryEpoch.Length is < 1 or > 128 || libraryEpoch.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-')))
                throw new ArgumentException("Invalid source library identity.", nameof(libraryEpoch));
            path += "?libraryEpoch=" + Uri.EscapeDataString(libraryEpoch);
        }
        var response = await SendAsync(serverId, method, path, method == HttpMethod.Post ? json ?? "{}" : null, ct);
        if (response.StatusCode is < 200 or >= 300)
            throw new HttpRequestException("The source request was refused.", null, (HttpStatusCode) response.StatusCode);
        return response.Body;
    }

    public async Task<ManagedServerResponse> SendAsync(string serverId, HttpMethod method, string pathAndQuery,
        string? json, CancellationToken ct = default)
    {
        ValidateApiPath(pathAndQuery);
        if (method != HttpMethod.Get && method != HttpMethod.Post && method != HttpMethod.Put &&
            method != HttpMethod.Patch && method != HttpMethod.Delete)
            throw new ArgumentException("This API method is not supported.", nameof(method));
        var maximumRequestBytes = method == HttpMethod.Post && ManagedResourceUploadLimits.IsOwnerUploadPath(pathAndQuery)
            ? ManagedResourceUploadLimits.MaximumEnvelopeBytes : 1024 * 1024;
        if (json != null && System.Text.Encoding.UTF8.GetByteCount(json) > maximumRequestBytes)
            throw new ArgumentException("The managed request is too large.", nameof(json));
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        budget.CancelAfter(_options.RequestTimeout);
        var (entry, clock, connected) = await ConnectVerifiedAsync(serverId, budget.Token);
        using var exchange = connected;
        using var request = new HttpRequestMessage(method, new Uri(new Uri(entry.BaseAddress), pathAndQuery));
        if (json != null)
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        if (!await TrySignAsync(request, entry, clock, budget.Token))
            throw new InvalidOperationException("This device is no longer paired with the source.");
        using var response = await exchange.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token);
        const int maximumBytes = 8 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > maximumBytes)
            throw new InvalidOperationException("The source response is too large.");
        await using var input = await response.Content.ReadAsStreamAsync(budget.Token);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer, budget.Token)) > 0)
        {
            if (output.Length + read > maximumBytes)
                throw new InvalidOperationException("The source response is too large.");
            output.Write(buffer, 0, read);
        }
        return new ManagedServerResponse((int) response.StatusCode,
            response.Content.Headers.ContentType?.MediaType ?? "application/json",
            System.Text.Encoding.UTF8.GetString(output.GetBuffer(), 0, (int) output.Length));
    }

    public async Task SendMediaAsync(string serverId, HttpMethod method, string pathAndQuery, string? range,
        Func<HttpResponseMessage, CancellationToken, Task> consume, CancellationToken ct = default)
    {
        ValidateApiPath(pathAndQuery);
        if (method != HttpMethod.Get && method != HttpMethod.Head)
            throw new ArgumentException("Media requests must use GET or HEAD.", nameof(method));
        System.Net.Http.Headers.RangeHeaderValue? parsedRange = null;
        if (!string.IsNullOrEmpty(range) &&
            (range.Length > 128 || !System.Net.Http.Headers.RangeHeaderValue.TryParse(range, out parsedRange) ||
             parsedRange.Unit != "bytes" || parsedRange.Ranges.Count != 1 ||
             parsedRange.Ranges.Any(r => r.From.HasValue && r.To.HasValue && r.From > r.To)))
            throw new ArgumentException("A single byte range is required.", nameof(range));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        using var headers = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        headers.CancelAfter(_options.RequestTimeout);
        var (entry, clock, connected) = await ConnectVerifiedAsync(serverId, headers.Token);
        using var exchange = connected;
        using var request = new HttpRequestMessage(method, new Uri(new Uri(entry.BaseAddress), pathAndQuery));
        request.Headers.Range = parsedRange;
        if (!await TrySignAsync(request, entry, clock, headers.Token))
            throw new InvalidOperationException("This device is no longer paired with the source.");
        using var response = await exchange.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headers.Token);
        // Header latency is bounded, but an already accepted movie may stream longer than fifteen seconds.
        headers.CancelAfter(Timeout.InfiniteTimeSpan);
        await consume(response, lifetime.Token);
    }

    public virtual Task<ManagedServerResponse> PlayManagedResourceAsync(string serverId, int resourceId, DataOrigin origin,
        string key, string libraryEpoch, bool explicitChoice = false, CancellationToken ct = default) =>
        throw new NotSupportedException("This host has no desktop player.");

    public virtual Task<ManagedServerResponse> OpenManagedResourceDirectoryAsync(string serverId, int resourceId,
        string libraryEpoch, CancellationToken ct = default) =>
        throw new NotSupportedException("This host has no desktop file manager.");

    public virtual Task<ManagedServerResponse> BatchPlayManagedResourcesAsync(string serverId, int[] resourceIds,
        string libraryEpoch, string? playerKey, int fileSelectionMode, CancellationToken ct = default) =>
        throw new NotSupportedException("This host has no desktop player.");

    private static void ValidateApiPath(string pathAndQuery)
    {
        if (string.IsNullOrEmpty(pathAndQuery) || pathAndQuery.Length > 8192 ||
            !pathAndQuery.StartsWith('/') || pathAndQuery.StartsWith("//") ||
            pathAndQuery.Contains('\\') || pathAndQuery.Contains('#') || pathAndQuery.Any(char.IsControl))
            throw new ArgumentException("A relative API path is required.", nameof(pathAndQuery));
    }

    private async Task<(ClientServerConnection Entry, ServerClock Clock, ServerExchange Exchange)> ConnectVerifiedAsync(
        string serverId, CancellationToken ct)
    {
        var entry = _store.Find(serverId) ?? throw new InvalidOperationException("This server is no longer managed.");
        var exchange = new ServerExchange(_options.Connector);
        try
        {
            var clock = ClockFor(serverId);
            var startedAt = DateTimeOffset.UtcNow;
            var handshake = await HandshakeAsync(exchange.Http, entry.BaseAddress, clock, ct);
            var identity = await IdentityOfAsync(serverId, entry.BaseAddress, handshake, exchange.Reached,
                startedAt, ct);
            if (!identity.IsConfirmed || !handshake.Succeeded)
                throw new InvalidOperationException("The source device could not be verified. Reconnect it before managing its resources.");
            // The handshake's connection pins the address; a forgotten/replaced pairing must not be reused.
            if (_store.Find(serverId) is not { } current || current.DeviceKey != entry.DeviceKey ||
                current.BaseAddress != entry.BaseAddress)
                throw new InvalidOperationException("The source connection changed. Try again.");
            return (entry, clock, exchange);
        }
        catch
        {
            exchange.Dispose();
            throw;
        }
    }

    public async Task<ManagedServersView> GetAsync(bool probe, CancellationToken ct = default)
    {
        if (probe)
        {
            await Task.WhenAll(_store.Read().Servers.Select(s => ProbeServerAsync(s, ct)));
        }

        // The requests before the servers. A collected request is dropped only once its server
        // is saved (CompleteClaimAsync), so a listing that no longer has the request has the
        // server. Read the other way round, a listing taken while the claim finished could have
        // neither: the page would take the request for rejected and stop watching.
        var requests = RequestViews();

        return new ManagedServersView(true, _store.Read().Servers.Select(ToView).ToList(), requests, CanOpenDesktopView);
    }

    public async Task<ManagedServerProbeView> ProbeAsync(string address, CancellationToken ct = default)
    {
        // As typed: the handshake reads it itself, and a missing port only shows before
        // normalizing puts a scheme in front.
        var handshake = await HandshakeAsync(address, new ServerClock(), ct);
        var outcome = await ClassifyAsync(address, handshake, ct);
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
        var found = await _discovery.DiscoverAsync(_options.DiscoveryTimeout, ct);
        var managed = _store.Read().Servers.Select(s => s.ServerId).ToHashSet(StringComparer.Ordinal);

        // Never this machine, however it answered — from loopback or from one of its own
        // addresses: its own server, right here. Managing yourself is what the app does without
        // a relay, and pairing with yourself is refused anyway. Another machine is listed even
        // under this install's own id: that is a copy of this data directory, and adding it
        // says so, which is the one way the user learns of it.
        var servers = found
            .Where(s => !string.IsNullOrWhiteSpace(s.ServerId) && !s.IsThisMachine)
            // One row per install: a server answering on two interfaces, or on both the
            // probe and mDNS, is still one server to pair with.
            .DistinctBy(s => s.ServerId, StringComparer.Ordinal)
            .Select(s => new ManagedServerCandidateView(s.ServerId, s.ServerName, s.BaseAddress, s.AppVersion,
                managed.Contains(s.ServerId), s.Kind, s.Platform))
            .ToList();

        return new ManagedServerDiscoveryView(servers);
    }

    public async Task<ManagedServerPairingView> PairAsync(string address, string? code, CancellationToken ct = default)
    {
        var normalized = ServerConnector.Normalize(address);
        var clock = new ServerClock();
        var handshake = await HandshakeAsync(address, clock, ct);
        var outcome = await ClassifyAsync(address, handshake, ct);

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

    /// <remarks>
    /// <see cref="ForgetAsync"/> without its last step, for every server at once: nothing is
    /// sent anywhere — no revoke, no handshake, no withdrawn request — because every key here is
    /// also the original install's, which still manages with it.
    /// </remarks>
    public async Task<int> ForgetAllLocallyAsync(CancellationToken ct = default)
    {
        // The filed requests first. One collected after the store is emptied would bring its
        // server back with a key filed by this copy; cancelled, a claim still waiting cannot
        // save (the store's write gate takes its token), and one already saving is ahead of
        // the write below, which then removes what it saved. A server simply sees its request
        // lapse, as when it is withdrawn from the page.
        var dropped = false;

        foreach (var requestId in _requests.Keys)
        {
            if (_requests.TryRemove(requestId, out var request))
            {
                request.Cancel();
                dropped = true;
            }
        }

        // A device that manages nothing, with no claim that could be saving a server, is left
        // with no store at all.
        List<ClientServerConnection> forgotten = !dropped && _store.Read().Servers.Count == 0
            ? []
            : await _store.MutateAsync(data =>
            {
                var servers = data.Servers.ToList();

                foreach (var server in servers)
                {
                    // As ForgetAsync keeps it: the browser still holds each server's storage
                    // under its origin, and a server paired again gets its origin back.
                    if (server.RelayPort is { } port)
                    {
                        (data.RetiredRelayPorts ??= new Dictionary<string, int>(StringComparer.Ordinal))
                            [server.ServerId] = port;
                    }
                }

                data.Servers.Clear();

                return servers;
            }, ct);

        // Whatever happens to the caller now: the keys are gone, and a relay left running would
        // sign with one it no longer has on record.
        await StopAllRelaysAsync();

        foreach (var server in forgotten)
        {
            _probes.TryRemove(server.ServerId, out _);
            _clocks.TryRemove(server.ServerId, out _);
            _identityAskedAt.TryRemove(server.ServerId, out _);
        }

        _logger.LogInformation(
            "Stopped managing {Count} servers here without telling them, for a new device identity: {ServerIds}",
            forgotten.Count, string.Join(", ", forgotten.Select(s => s.ServerId)));

        return forgotten.Count;
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

    public virtual Task<ManagedServerOpenView?> OpenAsync(string serverId, string? path, CancellationToken ct = default) =>
        throw new NotSupportedException("This host has no desktop relay. Manage resources from this server's own interface.");

    public ManagedServerState LastKnownState(string serverId) =>
        _probes.TryGetValue(serverId, out var probe) ? probe.State : ManagedServerState.Unknown;

    #endregion

    #region Pairing

    private Task<ServerHandshakeResult> HandshakeAsync(string address, ServerClock clock, CancellationToken ct) =>
        HandshakeAsync(_http, address, clock, ct);

    private async Task<ServerHandshakeResult> HandshakeAsync(HttpClient http, string address, ServerClock clock,
        CancellationToken ct)
    {
        try
        {
            return await new ServerConnector(http, clock, _self).HandshakeAsync(address, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return ServerHandshakeResult.NotReached(e);
        }
    }

    /// <summary>
    /// A client of the console's own rather than the app's: proxies are bypassed, as the relay's
    /// are — a system proxy has no route to a server on the LAN — a redirect is an answer to
    /// report, not one to follow, and a name's IPv4 and IPv6 addresses are raced
    /// (<see cref="DualStackConnector"/>), as the relay's are.
    /// </summary>
    private static HttpClient CreateHttpClient(
        Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> connect) =>
        new(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = connect,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

    /// <summary>
    /// One exchange with a managed server's address: asking who answers there, then whatever is
    /// signed for that server on the answer. Its first connection goes wherever the address
    /// connects, and every later one to the IP address that one reached (<see cref="Reached"/>),
    /// which is where a relay then connects too.
    /// </summary>
    /// <remarks>
    /// A name can resolve to several addresses, and those to several installs. Each connection
    /// racing them on its own, the one that said who it is and the one handed what is signed
    /// could differ — the confirmed one only has to be slower to accept once — so the answer
    /// holds for the IP address that gave it, not for the name.
    /// </remarks>
    private sealed class ServerExchange : IDisposable
    {
        private IPAddress? _reached;

        public ServerExchange(DualStackConnector connector)
        {
            Http = CreateHttpClient(async (context, ct) =>
            {
                if (Reached is { } reached)
                {
                    return await connector.ConnectAgainAsync(context.DnsEndPoint, reached, ct);
                }

                var (stream, to) = await connector.ConnectReachingAsync(context.DnsEndPoint, null, ct);
                Volatile.Write(ref _reached, to.Address);
                return stream;
            });
        }

        public HttpClient Http { get; }

        /// <summary>The IP address the exchange's first connection reached; null before one did.</summary>
        public IPAddress? Reached => Volatile.Read(ref _reached);

        public void Dispose() => Http.Dispose();
    }

    /// <summary>
    /// What a handshake means for managing, including the one case the handshake itself
    /// cannot see: an address that reached this device through a door other than its own
    /// ports — a LAN address of this machine, a name that resolves here.
    /// </summary>
    /// <remarks>
    /// This install's own id from another machine is another installation with the same
    /// identity — a copied data directory — and is refused as well, but said apart: "that is
    /// this computer" would send the user looking for a mistake in the address. See
    /// <see cref="IsCopyAsync"/>.
    /// </remarks>
    private async Task<ManagedServerOutcome> ClassifyAsync(string address, ServerHandshakeResult handshake,
        CancellationToken ct)
    {
        if (handshake.Server != null &&
            string.Equals(handshake.Server.Id, await _remoteAccess.GetOrCreateServerIdAsync(), StringComparison.Ordinal))
        {
            return await IsCopyAsync(address, handshake.Server, ct)
                ? ManagedServerOutcome.SameIdentity
                : ManagedServerOutcome.ThisDevice;
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
            ServerHandshakeOutcome.InvalidAddress => ManagedServerOutcome.InvalidAddress,
            ServerHandshakeOutcome.PortMissing => ManagedServerOutcome.PortMissing,
            ServerHandshakeOutcome.ProxyFakeAddress => ManagedServerOutcome.ProxyFakeAddress,
            _ => ManagedServerOutcome.NotBakabase
        };
    }

    /// <summary>
    /// Whether <paramref name="server"/>, which answered at <paramref name="address"/> with this
    /// install's own id, is another machine — a copy of this data directory — rather than this
    /// device.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two things a copy does not share with this device, and it takes both. The name a server
    /// gives itself is its machine's, which is not in the data directory: this device's own
    /// name is this device, reached through a door its address does not show — a reverse
    /// proxy or port forward on another host, the router's public address looping back. And
    /// the address has to be another machine's: one this machine holds leads here.
    /// </para>
    /// <para>
    /// Refused either way; this only chooses the words. So it never keeps a refusal from being
    /// made: an address it cannot read, or a name whose lookup runs out of time — in whatever
    /// budget the caller is spending — reads as this device, as every such answer did before
    /// the two were told apart.
    /// </para>
    /// </remarks>
    private async Task<bool> IsCopyAsync(string address, ServerInfo server, CancellationToken ct)
    {
        if (string.Equals(server.Name, (await _remoteAccess.GetServerDescriptorAsync()).Name,
                StringComparison.OrdinalIgnoreCase) ||
            RemoteAddressInput.Parse(address, out var root) != RemoteAddressProblem.None)
        {
            return false;
        }

        try
        {
            return !await _options.ReachesThisMachine(root!, ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
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
        _probes[server.Id] = new ProbeSnapshot(ManagedServerState.Online, server.Mode, server.AppVersion)
            { Kind = server.Kind, Platform = server.Platform };

        _logger.LogInformation("Now managing {ServerName} ({ServerId}) at {Address}", server.Name, server.Id,
            ServerConnector.Normalize(address));
    }

    private void StartClaiming(ClientPairingTicket ticket, string address, ServerInfo server, ServerClock clock)
    {
        var request = new PendingRequest(ticket.RequestId, address, server.Id, server.Name, ticket.ExpiresAt,
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

                if (await ClassifyAsync(request.Address, confirm, budget.Token) is
                    var outcome and (ManagedServerOutcome.ThisDevice or ManagedServerOutcome.SameIdentity))
                {
                    request.Finish(outcome);
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
        // next to a request still active, which only means one more read — the page knows the
        // request was collected when it is gone and its server is listed, whenever the server
        // appeared; one that showed the request ended before the server was saved would stop
        // the page watching for it. GetAsync reads the requests first for the same reason.
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
        using var exchange = new ServerExchange(_options.Connector);
        ProbeSnapshot snapshot;

        try
        {
            var handshake = await HandshakeAsync(exchange.Http, entry.BaseAddress, clock, budget.Token);
            var identity = await IdentityOfAsync(entry.ServerId, entry.BaseAddress, handshake, exchange.Reached,
                startedAt, budget.Token);
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
                        : server?.Mode ?? previous?.Mode, server?.AppVersion ?? previous?.AppVersion)
                {
                    Kind = server?.Kind ?? previous?.Kind,
                    Platform = server?.Platform ?? previous?.Platform,
                    OfflineReason = OfflineReasonOf(handshake)
                };
            }
            else
            {
                // What it says of itself now: a server that stopped saying (an older build)
                // has nothing more to show.
                snapshot = new ProbeSnapshot(await ReadContextAsync(exchange.Http, entry, clock, budget.Token),
                    server.Mode, server.AppVersion) { Kind = server.Kind, Platform = server.Platform };

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

            snapshot = new ProbeSnapshot(ManagedServerState.Offline, previous?.Mode, previous?.AppVersion)
                { Kind = previous?.Kind, Platform = previous?.Platform };
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
    /// Asks the server, signed as this device, what it makes of this device — over
    /// <paramref name="http"/>, the exchange in which it just said who it is.
    /// </summary>
    private async Task<ManagedServerState> ReadContextAsync(HttpClient http, ClientServerConnection entry,
        ServerClock clock, CancellationToken ct)
    {
        if (!Uri.TryCreate(entry.BaseAddress, UriKind.Absolute, out var root))
        {
            return ManagedServerState.Offline;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(root, "/remote-access/context"));

        if (!await TrySignAsync(request, entry, clock, ct))
        {
            // A stored key that does not decode manages nothing. Pairing again is the fix,
            // which is what Revoked sends the user to do.
            return ManagedServerState.Revoked;
        }

        using var response = await http.SendAsync(request, ct);

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
        using var exchange = new ServerExchange(_options.Connector);
        var handshake = await HandshakeAsync(exchange.Http, address, ClockFor(serverId), ct);
        var check = await IdentityOfAsync(serverId, address, handshake, exchange.Reached, startedAt, ct);

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
                        : previous?.Mode, previous?.AppVersion)
                    { Kind = previous?.Kind, Platform = previous?.Platform, OfflineReason = OfflineReasonOf(handshake) };
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
    /// <param name="reached">The IP address the handshake's connection reached, which the answer holds for.</param>
    private async Task<UpstreamIdentityCheck> IdentityOfAsync(string serverId, string address,
        ServerHandshakeResult handshake, IPAddress? reached, DateTimeOffset startedAt, CancellationToken ct)
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
            // This install's own id is this device unless another machine answers with it: a
            // copy of this data directory (see IsCopyAsync).
            var verdict =
                string.Equals(server.Id, await _remoteAccess.GetOrCreateServerIdAsync(), StringComparison.Ordinal)
                    ? await IsCopyAsync(address, server, ct)
                        ? UpstreamIdentityVerdict.SameIdentity
                        : UpstreamIdentityVerdict.ThisDevice
                    : string.Equals(server.Id, serverId, StringComparison.Ordinal)
                        ? UpstreamIdentityVerdict.Confirmed
                        : UpstreamIdentityVerdict.WrongServer;

            return new UpstreamIdentityCheck(serverId, address, verdict, server.Id, server.Name, null, startedAt)
                { ReachedAddress = reached };
        }

        return new UpstreamIdentityCheck(serverId, address, UpstreamIdentityVerdict.Unconfirmed, null, null,
            handshake.Outcome switch
            {
                ServerHandshakeOutcome.Unreachable => "nothing answers there",
                ServerHandshakeOutcome.RemoteAccessDisabled => "remote access is turned off there",
                ServerHandshakeOutcome.ProxyFakeAddress => "it leads into a proxy on this computer",
                _ => "what answers there is not a Bakabase server"
            }, startedAt)
        {
            // Its gate refused before saying who it is. What the user is told to do differs:
            // turn remote access on there, not check that something is running.
            RemoteAccessDisabled = handshake.Outcome == ServerHandshakeOutcome.RemoteAccessDisabled,
            // Nothing was asked at all: the fix is in this computer's proxy, not over there.
            ProxyFakeAddress = handshake.Outcome == ServerHandshakeOutcome.ProxyFakeAddress
        };
    }

    /// <summary>
    /// What a failed handshake says about why a server is offline, beyond that nothing answered:
    /// its address led into a proxy on this computer.
    /// </summary>
    private static ManagedServerOutcome? OfflineReasonOf(ServerHandshakeResult handshake) =>
        handshake.Outcome == ServerHandshakeOutcome.ProxyFakeAddress ? ManagedServerOutcome.ProxyFakeAddress : null;


    private static ProbeSnapshot Mismatched(ProbeSnapshot? previous, UpstreamIdentityCheck check) =>
        // The server's own mode, version, kind and platform as last seen, never the ones of
        // whoever answered in its place.
        new(ManagedServerState.WrongServer, previous?.Mode, previous?.AppVersion,
            new ManagedServerAnswerView(check.AnsweredById, check.AnsweredByName,
                check.Verdict == UpstreamIdentityVerdict.ThisDevice,
                check.Verdict == UpstreamIdentityVerdict.SameIdentity))
        {
            Kind = previous?.Kind,
            Platform = previous?.Platform
        };

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
        using var exchange = new ServerExchange(_options.Connector);

        try
        {
            // Only to the server itself. Whoever else answers at its address now has no
            // business receiving a request signed as this device — and one that takes any
            // caller as local would carry it out.
            var handshake = await HandshakeAsync(exchange.Http, entry.BaseAddress, ClockFor(entry.ServerId),
                budget.Token);
            var identity = await IdentityOfAsync(entry.ServerId, entry.BaseAddress, handshake, exchange.Reached,
                DateTimeOffset.UtcNow, budget.Token);

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

            using var response = await exchange.Http.SendAsync(request, budget.Token);

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

    protected ServerClock ClockFor(string serverId) => _clocks.GetOrAdd(serverId, _ => new ServerClock());

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
            probe?.State == ManagedServerState.WrongServer ? probe.AnsweredBy : null,
            probe?.Kind,
            probe?.Platform,
            probe?.State == ManagedServerState.Offline ? probe.OfflineReason : null);
    }

    #endregion

    protected static ClientServerConnection? Find(ClientConnectionData data, string serverId) =>
        data.Servers.FirstOrDefault(s => string.Equals(s.ServerId, serverId, StringComparison.Ordinal));

    /// <param name="AnsweredBy">Who answered at the address instead, while <paramref name="State"/> is WrongServer.</param>
    private sealed record ProbeSnapshot(ManagedServerState State, RemoteAccessMode? Mode, string? AppVersion,
        ManagedServerAnswerView? AnsweredBy = null)
    {
        /// <summary>What kind of install it said it is, the last time it answered as itself.</summary>
        public ServerKind? Kind { get; init; }

        /// <summary>What it said it runs on, the last time it answered as itself.</summary>
        public RemoteDevicePlatform? Platform { get; init; }

        /// <summary>Why it is offline, when more is known than that nothing answered.</summary>
        public ManagedServerOutcome? OfflineReason { get; init; }
    }

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
        string serverId,
        string? serverName,
        DateTime expiresAt,
        CancellationTokenSource cancellation)
    {
        private readonly Lock _gate = new();
        private ManagedServerOutcome _outcome = ManagedServerOutcome.AwaitingApproval;
        private DateTime? _finishedAt;

        public string RequestId { get; } = requestId;
        public string Address { get; } = address;
        public string ServerId { get; } = serverId;
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
                    _finishedAt == null && !cancellation.IsCancellationRequested, ServerId);
            }
        }
    }
}
