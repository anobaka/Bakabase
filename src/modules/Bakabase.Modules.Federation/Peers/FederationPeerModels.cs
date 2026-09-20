using Bakabase.Modules.Federation.Identity;

namespace Bakabase.Modules.Federation.Peers;

public sealed record NodePathMapping(string SourceRootId, string LocalPath);
public sealed record NodeGrantSummary(string GrantId, long Revision);
public sealed record FederationPeerView(string NodeId, string Label, string? Address, bool Enabled,
    string ConnectionState, NodeGrantSummary? OutboundGrant, NodeGrantSummary? InboundGrant,
    IReadOnlyList<NodePathMapping> PathMappings);
public sealed record NodePairingRequestView(string RequestId, string NodeId, string NodeName,
    string Direction, string Status, DateTimeOffset ExpiresAt);
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
}

public sealed record NodePairCodeRequest(string NodeId, string NodeName, string Code, string TransactionId,
    string ClaimSecret);
public sealed record NodePairRequest(string NodeId, string NodeName, string TransactionId, string ClaimSecret);
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
    public Dictionary<string, StoredPeer> Peers { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, StoredGrant> InboundGrants { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, NodeCredentials> OutboundGrants { get; set; } = new(StringComparer.Ordinal);
    public List<StoredPairRequest> IncomingRequests { get; set; } = [];
    public List<StoredOutgoingRequest> OutgoingRequests { get; set; } = [];
    public StoredInvitation? Invitation { get; set; }
}

internal sealed class StoredPeer
{
    public string NodeId { get; set; } = "";
    public string Label { get; set; } = "";
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
