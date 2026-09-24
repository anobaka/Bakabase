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
public sealed record FederationPeerView(string NodeId, string Label, string? Address, bool Enabled,
    string ConnectionState, NodeGrantSummary? OutboundGrant, NodeGrantSummary? InboundGrant,
    IReadOnlyList<NodePathMapping> PathMappings, ServerKind? Kind = null, RemoteDevicePlatform? Platform = null);
/// <param name="RemoteAddress">Where an incoming request came from; the claimed NodeId and name are unproven.</param>
/// <param name="ReplacesExistingAccess">Approving would replace a live grant already held under this NodeId.</param>
/// <param name="OffersReciprocalAccess">Approving also lets this device read the requester's library.</param>
public sealed record NodePairingRequestView(string RequestId, string NodeId, string NodeName,
    string Direction, string Status, DateTimeOffset ExpiresAt, string? RemoteAddress = null,
    bool ReplacesExistingAccess = false, bool OffersReciprocalAccess = false);
public sealed record FederationPeerStatus(NodeIdentity Identity, bool SharingEnabled,
    IReadOnlyList<FederationPeerView> Peers, IReadOnlyList<NodePairingRequestView> Requests);
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
public sealed record NodePairClaimRequest(string RequestId, string NodeId, string ClaimSecret);
public sealed record NodePairExchange(string Outcome, string RequestId, DateTimeOffset ExpiresAt,
    NodeCredentials? Credentials = null);
public sealed record NodeHandshakeRequest(string Challenge);
public sealed record NodeHandshakeResponse(NodeInfo Info, string Challenge, string Proof);

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
}
