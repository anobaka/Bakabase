using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.DataSync.TwoHost;

/// <summary>
/// One clock for both hosts (§13.7 "simulated clock"): the runtime's <see cref="IDataSyncClock"/> and the persistence
/// layer's <see cref="TimeProvider"/> read the same time, which only the test moves.
/// </summary>
internal sealed class TwoHostClock : TimeProvider, IDataSyncClock
{
    private long _ticks;

    /// <param name="start">UTC. The notification center stamps real local times, so the test starts near now.</param>
    public TwoHostClock(DateTime start) => _ticks = start.Ticks;

    public override DateTimeOffset GetUtcNow() => new(new DateTime(Interlocked.Read(ref _ticks), DateTimeKind.Utc));

    public DateTime UtcNow => GetUtcNow().UtcDateTime;

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}

/// <summary>
/// Federation between the two hosts as data sync sees it (§7.1, §7.2), in memory: who may read whose definitions,
/// definitions requests with their reciprocal offer (§7.2.4), the claim loop (§8.2: a granted request is raised
/// within 5 s) and the grant events the pairing flow raises. Reading goes through each host's
/// <see cref="Bakabase.TestKit.DataSync.InProcessPeerClient"/>, which a grant connects to the source's feed.
/// </summary>
internal sealed class TwoHostNetwork
{
    private readonly object _lock = new();
    private readonly Dictionary<string, TwoHostNode> _hosts = new(StringComparer.Ordinal);
    private readonly HashSet<(string Reader, string Source)> _grants = [];
    private readonly List<Request> _requests = [];
    private int _nextRequest = 1;

    public TwoHostNetwork(TwoHostClock clock) => Clock = clock;

    public TwoHostClock Clock { get; }

    /// <summary>When set, the next read-back fails with this error code (§7.2.4 N14).</summary>
    public string? FailNextReadBack { get; set; }

    private sealed class Request
    {
        public required string Id { get; init; }
        public required string From { get; init; }
        public required string To { get; init; }
        public required DataSyncRequestIntent Intent { get; init; }
        public required DateTime ExpiresAt { get; init; }
        public string Status { get; set; } = "pending";
        public bool Claimed { get; set; }

        /// <summary>A two-way request once approved: whether the approver reads the requester back (§7.2.4).</summary>
        public string? ReadBack { get; set; }
    }

    /// <summary>Adds a host, or puts a restarted one in the place of its earlier process, and connects its grants.</summary>
    public void Attach(TwoHostNode node)
    {
        lock (_lock)
        {
            _hosts[node.NodeId] = node;
            node.Client.Self = node.Device;
            foreach (var (reader, source) in _grants)
            {
                if (_hosts.TryGetValue(reader, out var r) && _hosts.TryGetValue(source, out var s))
                    r.Client.Connect(s.NodeId, s.Name, s.Services);
            }
        }
    }

    public IDataSyncGrantService GrantsFor(string nodeId) => new Grants(this, nodeId);

    public bool MayRead(string reader, string source)
    {
        lock (_lock) return _grants.Contains((reader, source));
    }

    /// <summary>
    /// The claim loop of <paramref name="nodeId"/> (§8.2, every 5 s in the Service): an approved request of its own it
    /// has not collected yet raises <see cref="IDataSyncGrantEvents.OutboundGranted"/>.
    /// </summary>
    public void Claim(string nodeId)
    {
        List<(string Peer, string? ReadBack)> granted;
        lock (_lock)
        {
            var approved = _requests.Where(r => r.From == nodeId && r.Status == "approved" && !r.Claimed).ToList();
            foreach (var request in approved) request.Claimed = true;
            granted = approved.Select(r => (r.To, r.ReadBack)).ToList();
        }

        foreach (var (peer, readBack) in granted) Events(nodeId).OutboundGranted(peer, readBack);
    }

    private TwoHostNode Host(string nodeId)
    {
        lock (_lock) return _hosts[nodeId];
    }

    private IDataSyncGrantEvents Events(string nodeId) => Host(nodeId).Services.GetRequiredService<IDataSyncGrantEvents>();

    private void Grant(string reader, string source)
    {
        lock (_lock)
        {
            _grants.Add((reader, source));
            var s = _hosts[source];
            _hosts[reader].Client.Connect(s.NodeId, s.Name, s.Services);
        }
    }

    private void Drop(string reader, string source)
    {
        lock (_lock)
        {
            _grants.Remove((reader, source));
            _hosts[reader].Client.Disconnect(source);
        }
    }

    /// <summary>One host's view of the network: the <see cref="IDataSyncGrantService"/> its runtime uses.</summary>
    private sealed class Grants(TwoHostNetwork network, string self) : IDataSyncGrantService
    {
        private readonly Dictionary<string, bool> _sharing = new(StringComparer.Ordinal);

        public Task<bool> IsSharingEnabledAsync(CancellationToken ct) =>
            Task.FromResult(_sharing.GetValueOrDefault(self, true));

        public Task SetSharingEnabledAsync(bool enabled, bool enablePairedRemoteAccess, CancellationToken ct)
        {
            _sharing[self] = enabled;
            return Task.CompletedTask;
        }

        public Task<RemoteAccessMode> GetRemoteAccessModeAsync(CancellationToken ct) =>
            Task.FromResult(RemoteAccessMode.Enabled);

        public Task<IReadOnlyList<DataSyncPeerCandidate>> GetPeersAsync(bool discover, CancellationToken ct)
        {
            lock (network._lock)
            {
                return Task.FromResult<IReadOnlyList<DataSyncPeerCandidate>>(network._hosts.Values
                    .Where(h => h.NodeId != self)
                    .Select(h => new DataSyncPeerCandidate(h.NodeId, h.Name, "inproc://" + h.NodeId, true, discover,
                        DataSyncContract.Version, true, network._grants.Contains((self, h.NodeId)),
                        network._grants.Contains((h.NodeId, self)), null, "Online"))
                    .ToList());
            }
        }

        public Task<DataSyncAccessRequestOutcome> RequestAccessAsync(DataSyncAccessRequestInput input,
            CancellationToken ct)
        {
            var peer = input.PeerNodeId ?? throw new NotSupportedException("The two-host network knows peers by id.");
            var target = network.Host(peer);
            lock (network._lock)
            {
                if (network._grants.Contains((self, peer)) && input.Intent == DataSyncRequestIntent.Follow)
                    return Task.FromResult(new DataSyncAccessRequestOutcome("granted", null, peer, target.Name, null));
                var request = new Request
                {
                    Id = "req-" + network._nextRequest++, From = self, To = peer, Intent = input.Intent,
                    ExpiresAt = network.Clock.UtcNow.AddDays(7),
                };
                network._requests.Add(request);
                return Task.FromResult(new DataSyncAccessRequestOutcome("awaitingApproval", request.Id, peer,
                    target.Name, null));
            }
        }

        public Task<IReadOnlyList<DataSyncAccessRequestView>> GetRequestsAsync(CancellationToken ct)
        {
            lock (network._lock)
            {
                return Task.FromResult<IReadOnlyList<DataSyncAccessRequestView>>(network._requests
                    .Where(r => r.From == self || r.To == self)
                    .Select(r =>
                    {
                        var incoming = r.To == self;
                        var other = network._hosts[incoming ? r.From : r.To];
                        return new DataSyncAccessRequestView(r.Id,
                            incoming ? DataSyncRequestDirection.Incoming : DataSyncRequestDirection.Outgoing,
                            other.NodeId, other.Name, r.Intent, r.Status, r.ExpiresAt, "inproc://" + other.NodeId,
                            false, null, false);
                    })
                    .ToList());
            }
        }

        /// <summary>
        /// Issues the grant, raises <see cref="IDataSyncGrantEvents.InboundGranted"/>, then reads the requester back
        /// when asked for a two-way request (§7.2.4), as <c>FederationDataSyncGrants.ApproveAsync</c> does.
        /// </summary>
        public Task<DataSyncApprovalOutcome> ApproveAsync(string requestId, bool readBack, CancellationToken ct)
        {
            Request request;
            lock (network._lock)
            {
                request = network._requests.SingleOrDefault(r => r.Id == requestId && r.To == self && r.Status == "pending")
                          ?? throw new DataSyncProblemException(new DataSyncProblem(DataSyncProblemCode.RequestNotFound,
                              null));
                request.Status = "approved";
            }

            network.Grant(request.From, self);
            var receiveBack = request.Intent == DataSyncRequestIntent.TwoWay && readBack;
            if (request.Intent == DataSyncRequestIntent.TwoWay) request.ReadBack = receiveBack ? "started" : "declined";
            var events = network.Events(self);
            events.InboundGranted(request.From, request.Intent, receiveBack);
            var granted = false;
            string? error = null;
            if (receiveBack)
            {
                if (network.FailNextReadBack is { } failure)
                {
                    network.FailNextReadBack = null;
                    error = failure;
                    events.ReadBackFailed(request.From, failure);
                }
                else
                {
                    network.Grant(self, request.From);
                    granted = true;
                    events.OutboundGranted(request.From);
                }
            }

            return Task.FromResult(new DataSyncApprovalOutcome(request.From, network.Host(request.From).Name,
                request.Intent, granted, error));
        }

        public Task RejectAsync(string requestId, CancellationToken ct) => End(requestId, "rejected", incoming: true);

        public Task CancelOutgoingAsync(string requestId, CancellationToken ct) =>
            End(requestId, "cancelled", incoming: false);

        private Task End(string requestId, string status, bool incoming)
        {
            lock (network._lock)
            {
                var request = network._requests.SingleOrDefault(r =>
                    r.Id == requestId && (incoming ? r.To : r.From) == self && r.Status == "pending");
                if (request is null)
                    throw new DataSyncProblemException(new DataSyncProblem(DataSyncProblemCode.RequestNotFound, null));
                request.Status = status;
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DataSyncGrantView>> GetGrantsAsync(CancellationToken ct)
        {
            lock (network._lock)
            {
                return Task.FromResult<IReadOnlyList<DataSyncGrantView>>(network._grants
                    .Where(g => g.Source == self)
                    .Select(g => new DataSyncGrantView(g.Reader, network._hosts[g.Reader].Name, network.Clock.UtcNow))
                    .ToList());
            }
        }

        public Task RevokeAsync(string peerNodeId, CancellationToken ct)
        {
            network.Drop(peerNodeId, self);
            return Task.CompletedTask;
        }

        public Task<DataSyncInvitationView> CreateInvitationAsync(DataSyncInvitationInput input, CancellationToken ct) =>
            throw new NotSupportedException("The two-host test pairs by request.");

        public Task<bool> HasOutboundGrantAsync(string peerNodeId, CancellationToken ct) =>
            Task.FromResult(network.MayRead(self, peerNodeId));

        public Task ForgetOutboundAsync(string peerNodeId, CancellationToken ct)
        {
            network.Drop(self, peerNodeId);
            return Task.CompletedTask;
        }
    }
}
