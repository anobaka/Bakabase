using System.Net;
using System.Text;
using System.Text.Json;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Transport;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;

namespace Bakabase.Modules.Federation.Tests.Security;

/// <summary>
/// Nodes on an in-memory network, for data sync's grants, pairing and sessions. Each node's routes pass the same
/// switch and scope rules as the Service's gate (<see cref="FederationRoutePolicy.RequiredSharing"/> before
/// authentication, <see cref="FederationRoutePolicy.ScopeMatches"/> after), then reach its real services.
/// </summary>
internal sealed class DataSyncTestNetwork : HttpMessageHandler
{
    public static readonly NodeDataSyncContract Contract = new(1, 1);
    private readonly Dictionary<string, DataSyncTestNode> _hosts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every request that reached a node, as <c>METHOD host path</c>.</summary>
    public List<string> Requests { get; } = [];

    public HttpClient Client { get; }
    public FederationHttpClient Wire { get; }

    public DataSyncTestNetwork()
    {
        Client = new HttpClient(this);
        Wire = new FederationHttpClient(Client);
    }

    public DataSyncTestNode Add(string nodeId, string? host = null, IServerSelfDescription? self = null)
    {
        var node = new DataSyncTestNode(nodeId, this, self);
        _hosts[host ?? nodeId] = node;
        return node;
    }

    /// <summary>Puts <paramref name="node"/> at <paramref name="host"/>, or takes the host off the network.</summary>
    public void Route(string host, DataSyncTestNode? node)
    {
        if (node == null) _hosts.Remove(host);
        else _hosts[host] = node;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!_hosts.TryGetValue(request.RequestUri!.Host, out var node))
            throw new HttpRequestException("No route to host.");
        var path = request.RequestUri.AbsolutePath;
        lock (Requests) Requests.Add($"{request.Method.Method} {request.RequestUri.Host} {path}");
        var bytes = request.Content == null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
        T Body<T>() => JsonSerializer.Deserialize<T>(bytes, FederationJson.Options)!;
        try
        {
            var kind = FederationRoutePolicy.Classify(path) ??
                       throw new FederationAccessException("NodeRouteForbidden", 403, "Outside the protocol.");
            var required = FederationRoutePolicy.RequiredSharing(kind, request.Method.Method, path) ??
                           throw new FederationAccessException("NodeRouteForbidden", 403, "Outside the protocol.");
            var switches = await node.Store.GetSharingSwitchesAsync(ct);
            if (required == FederationSharingRequirement.Library && !switches.Library ||
                required is FederationSharingRequirement.Either or FederationSharingRequirement.GrantScope &&
                !switches.Library && !switches.DataSync)
                throw new FederationAccessException("SharingDisabled", 403, "Sharing is disabled.");
            if (required == FederationSharingRequirement.DataSync && !switches.DataSync)
                throw NodeGrantService.DataSyncSharingDisabled();
            object value;
            if (path == "/federation/v1/info") value = await node.InfoAsync(ct);
            else if (path == "/federation/v1/pair/code") value = await node.Peers.ExchangeCodeAsync(Body<NodePairCodeRequest>(), node.Address, ct);
            else if (path == "/federation/v1/pair/request") value = await node.Peers.RequestPairingAsync(Body<NodePairRequest>(), node.Address, ct);
            else if (path == "/federation/v1/pair/claim") value = await node.Peers.ClaimPairingAsync(Body<NodePairClaimRequest>(), ct);
            else if (path == "/federation/v1/pair/datasync/request")
                value = (await node.Peers.SubmitDataSyncRequestAsync(Body<NodeDataSyncPairRequest>(), node.Address, ct)).Exchange;
            else if (path == "/federation/v1/pair/datasync/code")
            {
                var code = Body<NodeDataSyncPairCodeRequest>();
                var (exchange, issued, intent) = await node.Peers.ExchangeDataSyncCodeAsync(code, node.Address, ct);
                if (issued) node.Redeemed.Add((code.NodeId, intent, exchange.ReadBack));
                value = exchange;
            }
            else if (path == "/federation/v1/pair/datasync/claim") value = await node.Peers.ClaimDataSyncAsync(Body<NodePairClaimRequest>(), ct);
            else
            {
                var query = request.RequestUri.Query;
                var principal = await node.Auth.AuthenticateAsync(request.Headers.GetValues("Authorization").Single(),
                    request.Method.Method, path, query.Length == 0 ? "" : query[1..], NodeRequestSignature.Hash(bytes), ct);
                if (!FederationRoutePolicy.ScopeMatches(required, principal.Scope)) throw NodeGrantService.ScopeNotGranted();
                value = path.EndsWith("/handshake")
                    ? await node.Grants.CreateHandshakeAsync(principal.GrantId, Body<NodeHandshakeRequest>().Challenge, ct)
                    : new { nodeId = node.Id, scope = principal.Scope };
            }
            return Json(HttpStatusCode.OK, value);
        }
        catch (FederationAccessException e)
        {
            return Json((HttpStatusCode)e.StatusCode, new { code = e.ErrorCode, message = e.Message });
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object value) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(value, FederationJson.Options), Encoding.UTF8,
            "application/json")
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            foreach (var node in _hosts.Values.Distinct()) node.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class DataSyncTestNode : IFederationDataDirectory, INodeIdSource, IDisposable
{
    private readonly DataSyncTestNetwork _network;

    public DataSyncTestNode(string id, DataSyncTestNetwork network, IServerSelfDescription? self = null)
    {
        Id = id;
        _network = network;
        Self = self;
        Store = new FederationStateStore(this, this);
        Identity = new NodeIdentityProvider(Store);
        Peers = new FederationPeerService(Store, Identity, Leases, Clock);
        Grants = new NodeGrantService(Store, Identity, Leases, Clock, self);
        Auth = new NodeGrantAuthenticator(Grants, new NodeNonceCache(Clock), Clock);
        Pairing = new NodePairingClient(Store, Identity, network.Wire, Clock, Leases, Peers);
    }

    public string Id { get; }
    public IServerSelfDescription? Self { get; }
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
        "federation-datasync-" + Guid.NewGuid().ToString("N"));
    public TestClock Clock { get; } = new();
    public GrantLeaseRegistry Leases { get; } = new();
    public FederationStateStore Store { get; }
    public INodeIdentityProvider Identity { get; }
    public FederationPeerService Peers { get; }
    public NodeGrantService Grants { get; }
    public NodeGrantAuthenticator Auth { get; }
    public NodePairingClient Pairing { get; }

    /// <summary>The address requests to this node seem to come from.</summary>
    public string? Address { get; set; }

    /// <summary>Whether its info says it speaks data sync (a build from before it says nothing).</summary>
    public int? ContractVersion { get; set; } = 1;
    public int? MinimumPeerContract { get; set; } = 1;

    /// <summary>Datasync codes this node granted: the redeemer, its intent, and what the exchange said about read-back.</summary>
    public List<(string NodeId, string Intent, string? ReadBack)> Redeemed { get; } = [];

    public string Ensure()
    {
        Directory.CreateDirectory(Path);
        return Path;
    }

    public Task<string> GetNodeIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(Id);

    public PeerSessionFactory Sessions(INodePeerDiscovery? discovery = null) =>
        new(Store, Identity, _network.Wire, Clock, discovery);

    public NodeTransport Transport(PeerSessionFactory sessions) => new(Store, sessions, _network.Wire, Clock, Leases);

    public async Task<NodeInfo> InfoAsync(CancellationToken ct = default)
    {
        var local = await Identity.GetAsync(ct);
        var info = new NodeInfo(local.NodeId, local.LibraryEpoch, local.Name, 1, Clock.GetUtcNow()).DescribedBy(Self);
        return ContractVersion == null
            ? info
            : info with
            {
                DataSyncContractVersion = ContractVersion, DataSyncMinimumPeerContract = MinimumPeerContract,
                DataSyncKinds = ["customProperty@1"], SharesDefinitions = await Store.IsDataSyncSharingEnabledAsync(ct)
            };
    }

    /// <summary>A library grant from this node to <paramref name="reader"/>, made the way a person makes one.</summary>
    public async Task<NodeCredentials> GrantLibraryAsync(DataSyncTestNode reader)
    {
        await Peers.SetSharingAsync(true);
        var code = (await Peers.IssueInvitationAsync()).Code;
        Assert.AreEqual("granted", (await reader.Pairing.ConnectAsync("http://" + Id, code)).Outcome);
        return reader.Outbound(Id)!;
    }

    /// <summary>A <c>datasync.read</c> grant from this node to <paramref name="reader"/>, by request and approval.</summary>
    public async Task<NodeCredentials> GrantDataSyncAsync(DataSyncTestNode reader, string intent = NodeDataSyncIntents.Follow)
    {
        await Peers.SetDataSyncSharingAsync(true);
        var pending = await reader.Pairing.ConnectDataSyncAsync("http://" + Id, null, intent, DataSyncTestNetwork.Contract);
        Assert.AreEqual("awaitingApproval", pending.Outcome);
        await Peers.ApproveDataSyncAsync(pending.RequestId);
        Assert.AreEqual("granted", (await reader.Pairing.ClaimDataSyncAsync(pending.RequestId)).Outcome);
        return reader.OutboundDataSync(Id)!;
    }

    public void Dispose()
    {
        Leases.Dispose();
        if (Directory.Exists(Path)) Directory.Delete(Path, true);
    }
}

internal sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

internal static class DataSyncTestStateExtensions
{
    /// <summary>The state file as written, for reading what a test cannot see through a service.</summary>
    public static JsonElement ReadState(this DataSyncTestNode node) =>
        JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(node.Path, FederationStateStore.FileName))).RootElement;

    public static JsonElement Peer(this DataSyncTestNode node, string nodeId) =>
        node.ReadState().GetProperty("peers").GetProperty(nodeId);

    /// <summary>This node's library credentials for <paramref name="nodeId"/>, as stored.</summary>
    public static NodeCredentials? Outbound(this DataSyncTestNode node, string nodeId) =>
        Read(node, "outboundGrants", nodeId);

    /// <summary>This node's datasync credentials for <paramref name="nodeId"/>, as stored.</summary>
    public static NodeCredentials? OutboundDataSync(this DataSyncTestNode node, string nodeId) =>
        Read(node, "outboundDataSyncGrants", nodeId);

    private static NodeCredentials? Read(DataSyncTestNode node, string collection, string nodeId) =>
        node.ReadState().GetProperty(collection).TryGetProperty(nodeId, out var credentials)
            ? credentials.Deserialize<NodeCredentials>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            : null;
}
