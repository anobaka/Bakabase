using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;

namespace Bakabase.Modules.Federation.Peers;

public sealed record NodePathMapping(string SourceRootId, string LocalPath);
public sealed record NodeGrantSummary(string GrantId, long Revision);
/// <param name="Kind">
/// What kind of install it is, as its last verified handshake said — for showing. Null until one
/// did, and for peers too old to say.
/// </param>
/// <param name="Platform">What it runs on, as its last verified handshake said.</param>
/// <param name="OutboundGrant">This device's library read access to the peer; library access only.</param>
/// <param name="InboundGrant">The peer's library read access to this device; library access only.</param>
/// <param name="InboundDataSyncGrant">The peer's <c>datasync.read</c> access to this device's definitions.</param>
/// <param name="OutboundDataSyncGrant">This device's <c>datasync.read</c> access to the peer's definitions.</param>
public sealed record FederationPeerView(string NodeId, string Label, string? Address, bool Enabled,
    string ConnectionState, NodeGrantSummary? OutboundGrant, NodeGrantSummary? InboundGrant,
    IReadOnlyList<NodePathMapping> PathMappings, ServerKind? Kind = null, RemoteDevicePlatform? Platform = null,
    NodeGrantSummary? InboundDataSyncGrant = null, NodeGrantSummary? OutboundDataSyncGrant = null);
/// <param name="RemoteAddress">Where an incoming request came from; the claimed NodeId and name are unproven.</param>
/// <param name="ReplacesExistingAccess">Approving would replace a live grant already held under this NodeId.</param>
/// <param name="OffersReciprocalAccess">Approving also lets this device read the requester's library.</param>
public sealed record NodePairingRequestView(string RequestId, string NodeId, string NodeName,
    string Direction, string Status, DateTimeOffset ExpiresAt, string? RemoteAddress = null,
    bool ReplacesExistingAccess = false, bool OffersReciprocalAccess = false);
/// <param name="SharingEnabled">Library sharing.</param>
/// <param name="DataSyncSharingEnabled">Whether devices this one approved may read its definitions.</param>
public sealed record FederationPeerStatus(NodeIdentity Identity, bool SharingEnabled,
    IReadOnlyList<FederationPeerView> Peers, IReadOnlyList<NodePairingRequestView> Requests,
    bool DataSyncSharingEnabled = false);
public sealed record NodePairingOutcome(string Outcome, string? RequestId = null, string? PeerNodeId = null,
    string? Message = null);
public sealed record NodeInvitation(string Code, DateTimeOffset ExpiresAt);

/// <summary>Credentials for exactly one direction. Never return this from a local UI API.</summary>
public sealed record NodeCredentials(string GrantId, string SubjectNodeId, string AudienceNodeId,
    string LibraryEpoch, string Key, long Revision);

public sealed record NodeInfo(string NodeId, string LibraryEpoch, string Name, int ProtocolVersion,
    DateTimeOffset ServerTimeUtc)
{
    public int QueryContractVersion { get; init; } = 1;
    public string[] SupportedFilters { get; init; } = ["text", "fileAvailability", "sourceKinds"];
    public string[] SupportedSorts { get; init; } = ["NameAsc", "NameDesc"];
    public string[] SupportedAssetKinds { get; init; } = ["image", "audio", "video"];
    public int MaxBatchSize { get; init; } = 200;

    /// <summary>
    /// Optional, added later, for showing only: what kind of install this is, as a word
    /// (<see cref="ServerSelfDescriptionWords"/>) so a later kind never breaks an older reader.
    /// Older nodes leave it out. Not covered by the handshake proof, which signs a fixed field
    /// list; nothing is decided on it.
    /// </summary>
    public string? Kind { get; init; }

    /// <summary>Optional, like <see cref="Kind"/>: the operating system it runs on, as a word.</summary>
    public string? Platform { get; init; }

    /// <summary>
    /// Optional, added for data sync: the data sync contract version this node speaks. Null on nodes
    /// without data sync. Not covered by the handshake proof, which signs a fixed field list; the
    /// host's <see cref="INodeInfoContributor"/> fills the data sync members.
    /// </summary>
    public int? DataSyncContractVersion { get; init; }

    /// <summary>Optional: the lowest data sync contract version a peer must speak to read this node.</summary>
    public int? DataSyncMinimumPeerContract { get; init; }

    /// <summary>Optional: the kinds of definition this node publishes, as <c>kind@schemaVersion</c>.</summary>
    public string[]? DataSyncKinds { get; init; }

    /// <summary>Optional: whether devices this node approved may read its definitions right now.</summary>
    public bool? SharesDefinitions { get; init; }

    /// <summary>This node's info saying what it is, when the host can tell.</summary>
    public NodeInfo DescribedBy(IServerSelfDescription? self) => self == null
        ? this
        : this with
        {
            Kind = ServerSelfDescriptionWords.Of(self.Kind),
            Platform = ServerSelfDescriptionWords.Of(self.Platform)
        };
}

/// <summary>
/// Offered by a requester that also shares its own library: where the source can reach it and a
/// single-use code that grants the source read access back, so one approval pairs both ways.
/// </summary>
public sealed record NodeReciprocalOffer(string[] Addresses, string Code);
public sealed record NodePairCodeRequest(string NodeId, string NodeName, string Code, string TransactionId,
    string ClaimSecret, NodeReciprocalOffer? Reciprocal = null);
public sealed record NodePairRequest(string NodeId, string NodeName, string TransactionId, string ClaimSecret,
    NodeReciprocalOffer? Reciprocal = null);
/// <summary>
/// A request for a <c>datasync.read</c> grant (<c>pair/datasync/request</c>). A separate route and
/// record from the library's, so an older node refuses it instead of filing a library request.
/// </summary>
/// <param name="Intent"><c>"follow"</c> (read the source's definitions) or <c>"twoWay"</c> (keep in step both ways).</param>
/// <param name="Reciprocal">With <c>twoWay</c>: where the source can reach the requester, and a single-use
/// datasync code that lets the source read the requester's definitions back.</param>
public sealed record NodeDataSyncPairRequest(string NodeId, string NodeName, string TransactionId,
    string ClaimSecret, string Intent, NodeReciprocalOffer? Reciprocal = null);
/// <summary>A datasync invitation code redeemed on <c>pair/datasync/code</c>; never a library code.</summary>
/// <param name="Intent"><c>"follow"</c> or <c>"twoWay"</c>, as in <see cref="NodeDataSyncPairRequest"/>.</param>
public sealed record NodeDataSyncPairCodeRequest(string NodeId, string NodeName, string Code, string TransactionId,
    string ClaimSecret, string Intent, NodeReciprocalOffer? Reciprocal = null);
public sealed record NodePairClaimRequest(string RequestId, string NodeId, string ClaimSecret);
/// <param name="ReadBack">
/// Datasync codes only: <c>"started"</c> when the code's creator reads the redeemer back, <c>"declined"</c>
/// when a two-way redemption met a code created without two-way consent; null otherwise.
/// </param>
public sealed record NodePairExchange(string Outcome, string RequestId, DateTimeOffset ExpiresAt,
    NodeCredentials? Credentials = null, string? ReadBack = null);
public sealed record NodeHandshakeRequest(string Challenge);
/// <param name="Scope">
/// The scope of the grant the handshake was made with (<see cref="FederationScopes"/>). Informational and
/// outside the proof: the issuer enforces scopes, never the reader.
/// </param>
public sealed record NodeHandshakeResponse(NodeInfo Info, string Challenge, string Proof, string? Scope = null);

/// <summary>What a datasync request asks for (<see cref="NodeDataSyncPairRequest.Intent"/>).</summary>
public static class NodeDataSyncIntents
{
    /// <summary>Read the source's definitions.</summary>
    public const string Follow = "follow";

    /// <summary>Keep definitions in step both ways: the source may also read the requester back.</summary>
    public const string TwoWay = "twoWay";

    public static bool IsValid(string? intent) => intent is Follow or TwoWay;
}

/// <summary>
/// What a datasync code redemption says about reading the redeemer back (<see cref="NodePairExchange.ReadBack"/>).
/// </summary>
public static class NodeDataSyncReadBack
{
    /// <summary>The code's creator agreed to two-way when it made the code, and reads the redeemer back.</summary>
    public const string Started = "started";

    /// <summary>A two-way redemption of a code made without two-way consent: the grant only.</summary>
    public const string Declined = "declined";
}

/// <summary>
/// The data sync contract of this build, as the host knows it: the federation module does not know data sync, so
/// the requester's checks of a peer's <see cref="NodeInfo"/> (§7.2.2) take it from the caller.
/// </summary>
public sealed record NodeDataSyncContract(int Version, int MinimumPeerVersion);

/// <summary>How a datasync pairing attempt ended, on the requesting device.</summary>
/// <param name="Outcome"><c>"granted"</c>, <c>"awaitingApproval"</c> or <c>"rejected"</c>.</param>
/// <param name="PeerName">The name the peer's <c>/info</c> gave.</param>
/// <param name="ReadBack">For a code: whether the peer reads this device back (<see cref="NodeDataSyncReadBack"/>).</param>
public sealed record NodeDataSyncPairingOutcome(string Outcome, string RequestId, string PeerNodeId, string PeerName,
    string? ReadBack = null);

/// <summary>An approved datasync request, as the approving device's own flow needs it.</summary>
/// <param name="HasReciprocal">A two-way request whose offer to be read back is still there to take.</param>
public sealed record NodeDataSyncApproval(string NodeId, string NodeName, string Intent, bool HasReciprocal);

/// <summary>A datasync request, for this device's own management plane only.</summary>
/// <param name="Direction"><c>"incoming"</c> or <c>"outgoing"</c>.</param>
/// <param name="RemoteAddress">Incoming: where it came from (its NodeId and name are only a claim). Outgoing: where it was sent.</param>
/// <param name="KnownAddress">
/// Incoming: where this device already knows the node the request claims to be, when it knows one.
/// </param>
/// <param name="ReplacesExistingAccess">Approving would replace a live datasync grant held under this NodeId.</param>
public sealed record NodeDataSyncRequestView(string RequestId, string Direction, string NodeId, string NodeName,
    string Intent, string Status, DateTimeOffset ExpiresAt, string? RemoteAddress, string? KnownAddress,
    bool ReplacesExistingAccess);

/// <summary>A live <c>datasync.read</c> grant this device issued: who may read its definitions.</summary>
public sealed record NodeDataSyncGrantView(string NodeId, string Name, string GrantId, DateTimeOffset GrantedAt);

/// <summary>A device known here, as data sync sees it.</summary>
/// <param name="Address">Where data sync reaches it: its own address if it has one, else the library's.</param>
/// <param name="WeMayRead">This device holds a <c>datasync.read</c> grant for it.</param>
/// <param name="TheyMayRead">It holds a live <c>datasync.read</c> grant for this device.</param>
public sealed record NodeDataSyncPeerView(string NodeId, string Name, string? Address, bool WeMayRead,
    bool TheyMayRead);

/// <summary>Everything about <c>datasync.read</c> this device's own management plane shows; never a key or a code.</summary>
public sealed record FederationDataSyncStatus(bool SharingEnabled, IReadOnlyList<NodeDataSyncPeerView> Peers,
    IReadOnlyList<NodeDataSyncRequestView> Requests, IReadOnlyList<NodeDataSyncGrantView> Grants);

internal sealed class FederationState
{
    public int SchemaVersion { get; set; } = 1;
    public string? NodeId { get; set; }
    public string? LibraryEpoch { get; set; }
    public bool SharingEnabled { get; set; }
    public bool BrowsingEnabled { get; set; }
    public Dictionary<string, StoredPeer> Peers { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, StoredGrant> InboundGrants { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, NodeCredentials> OutboundGrants { get; set; } = new(StringComparer.Ordinal);
    public List<StoredPairRequest> IncomingRequests { get; set; } = [];
    public List<StoredOutgoingRequest> OutgoingRequests { get; set; } = [];
    public StoredInvitation? Invitation { get; set; }
    public List<StoredReciprocalInvitation> ReciprocalInvitations { get; set; } = [];
    /// <summary>User-chosen name shown to other devices; the machine name otherwise.</summary>
    public string? DisplayName { get; set; }

    // Data sync (datasync.read) state. Kept apart from the library collections above, so an older
    // build, which ignores unknown members, can never read a datasync grant, request or code as a
    // library one. Missing (or null) in older files; FederationStateStore reads that as empty.
    public bool DataSyncSharingEnabled { get; set; }
    /// <summary>By grant id; grant ids never collide with <see cref="InboundGrants"/>.</summary>
    public Dictionary<string, StoredGrant> InboundDataSyncGrants { get; set; } = new(StringComparer.Ordinal);
    /// <summary>By the peer's node id.</summary>
    public Dictionary<string, NodeCredentials> OutboundDataSyncGrants { get; set; } = new(StringComparer.Ordinal);
    public List<StoredPairRequest> IncomingDataSyncRequests { get; set; } = [];
    public List<StoredOutgoingRequest> OutgoingDataSyncRequests { get; set; } = [];
    public StoredInvitation? DataSyncInvitation { get; set; }
    public List<StoredReciprocalInvitation> DataSyncReciprocalInvitations { get; set; } = [];

    /// <summary>
    /// Whether library routing belongs to this peer: a library grant in either direction. Data sync never writes
    /// such a peer's label, address, library epoch, kind, platform or browsing switch (§7.1.1), so a definitions
    /// pairing, even one approved for a device claiming this NodeId, can never redirect library browsing.
    /// </summary>
    public bool HasLibraryGrant(string nodeId) => OutboundGrants.ContainsKey(nodeId) ||
        InboundGrants.Values.Any(g => !g.Revoked && g.Credentials.SubjectNodeId == nodeId);
}

internal sealed class StoredReciprocalInvitation
{
    public string CodeHash { get; set; } = "";
    public string AudienceNodeId { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
}

internal sealed class StoredPeer
{
    public string NodeId { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>What its last verified handshake said it is; kept so an offline peer still shows it.</summary>
    public ServerKind? Kind { get; set; }
    public RemoteDevicePlatform? Platform { get; set; }
    public string? Address { get; set; }
    public string? LibraryEpoch { get; set; }
    public bool Enabled { get; set; } = true;
    public List<NodePathMapping> PathMappings { get; set; } = [];
    /// <summary>
    /// Where data sync reaches this peer; data sync connects to <c>DataSyncAddress ?? Address</c> and relocates
    /// only this. Library code never reads it.
    /// </summary>
    public string? DataSyncAddress { get; set; }
    /// <summary>
    /// Where the peer said it could be read back when this device took its two-way offer (§7.2.4). Unverified: never
    /// a session's address, only tried, with the peer's NodeId expected, to ask it for its definitions again when no
    /// other address is known ("Try again" after a failed read-back). Dropped once a datasync address is verified.
    /// </summary>
    public string[]? DataSyncOfferedAddresses { get; set; }
}

internal sealed class StoredGrant
{
    public NodeCredentials Credentials { get; set; } = null!;
    public bool Revoked { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class StoredInvitation
{
    public string CodeHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public int FailedAttempts { get; set; }
    /// <summary>Datasync invitations only: whoever redeems it may also be read back (two-way consent).</summary>
    public bool AllowTwoWay { get; set; }
}

internal sealed class StoredPairRequest
{
    public string RequestId { get; set; } = "";
    public string TransactionId { get; set; } = "";
    public string NodeId { get; set; } = "";
    public string NodeName { get; set; } = "";
    public string ClaimSecretHash { get; set; } = "";
    public string Status { get; set; } = "awaitingApproval";
    public DateTimeOffset ExpiresAt { get; set; }
    public string? GrantId { get; set; }
    public string? RemoteAddress { get; set; }
    public NodeReciprocalOffer? Reciprocal { get; set; }
    /// <summary>Datasync requests only: <c>"follow"</c> or <c>"twoWay"</c>.</summary>
    public string? Intent { get; set; }
    /// <summary>
    /// Datasync code redemptions only: what the exchange said about reading the redeemer back
    /// (<see cref="NodeDataSyncReadBack"/>), so a re-delivered exchange says the same.
    /// </summary>
    public string? ReadBack { get; set; }
}

internal sealed class StoredOutgoingRequest
{
    public string RequestId { get; set; } = "";
    public string Address { get; set; } = "";
    public string NodeId { get; set; } = "";
    public string NodeName { get; set; } = "";
    public string LibraryEpoch { get; set; } = "";
    public string ClaimSecret { get; set; } = "";
    public string Status { get; set; } = "awaitingApproval";
    public DateTimeOffset ExpiresAt { get; set; }
    /// <summary>Datasync requests only: <c>"follow"</c> or <c>"twoWay"</c>.</summary>
    public string? Intent { get; set; }
}
