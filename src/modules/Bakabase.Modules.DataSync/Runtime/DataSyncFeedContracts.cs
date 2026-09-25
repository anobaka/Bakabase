using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Runtime;

// §2.9: the feed (source side [C], receiver side [D]) and the grant service [D].

/// <summary>What a reader tells the source with head and manifest (§7.5).</summary>
public sealed record DataSyncFeedQuery(string? Mode /* "follow"|"twoWay" */, IReadOnlyDictionary<string, long> Since,
    string? ReaderActorId, string? ReaderState /* "ok"|"awaitingReview"|"paused:{reason}"|"needsYou:{n}" */);

public sealed record DataSyncReader(string NodeId, string GrantId, string Name);

/// <summary>Source side of the feed [C].</summary>
public interface IDataSyncFeedSource
{
    /// <summary>Takes the DataSyncGate (≤ 30 s, else DataSyncFeedException("Busy", 503, retryable)).</summary>
    Task<DataSyncFeedHead> GetHeadAsync(DataSyncReader reader, DataSyncFeedQuery query, CancellationToken ct);

    /// <summary>Takes the gate; builds the whole snapshot while holding it (§7.5.2).</summary>
    Task<DataSyncFeedManifest> CreateSnapshotAsync(DataSyncReader reader, DataSyncFeedQuery query, CancellationToken ct);

    /// <summary>Returns a precomputed page's canonical bytes (§7.5.3). Never takes the gate.</summary>
    Task<byte[]> GetPageAsync(DataSyncReader reader, string snapshotId, string kind, long sinceSeq, string? cursor,
        CancellationToken ct);
}

public sealed class DataSyncFeedException(string code, int status, bool retryable = false, int? retryAfterSeconds = null)
    : Exception(code)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public bool Retryable { get; } = retryable;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}

/// <summary>Receiver side of the feed [D over PeerSessionFactory, datasync scope]. Throws DataSyncPeerException.</summary>
public interface IDataSyncPeerClient
{
    Task<DataSyncPeerProbe> ProbeAsync(string peerNodeId, CancellationToken ct);
    Task<DataSyncFeedHead> GetHeadAsync(string peerNodeId, DataSyncFeedQuery query, CancellationToken ct);
    Task<DataSyncFeedManifest> GetManifestAsync(string peerNodeId, DataSyncFeedQuery query, CancellationToken ct);

    Task<ReadOnlyMemory<byte>> GetPageAsync(string peerNodeId, string snapshotId, string kind, long sinceSeq,
        string? cursor, CancellationToken ct);
}

public sealed record DataSyncPeerProbe(string NodeId, string Name, string? Address, bool HasAccess,
    int? ContractVersion, bool? SharesDefinitions, string? ConnectionState);

public sealed class DataSyncPeerException(DataSyncPeerErrorCode code, string? detail = null, int? retryAfterSeconds = null)
    : Exception(detail ?? code.ToString())
{
    public DataSyncPeerErrorCode Code { get; } = code;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}

/// <summary>Grants, requests and invitations for datasync.read [D over FederationPeerService/NodePairingClient].</summary>
public interface IDataSyncGrantService
{
    Task<bool> IsSharingEnabledAsync(CancellationToken ct);

    /// <summary>
    /// enablePairedRemoteAccess: turns remote access on with pairing required only when the effective mode is
    /// Disabled, performed in the Service by FederationDataSyncGrants (as the controller does); never touches Enabled
    /// or Unrestricted (§7.1.3). Turning sharing on while remote access stays Disabled is allowed but reported (§7.2.4).
    /// </summary>
    Task SetSharingEnabledAsync(bool enabled, bool enablePairedRemoteAccess, CancellationToken ct);

    Task<RemoteAccessMode> GetRemoteAccessModeAsync(CancellationToken ct);
    Task<IReadOnlyList<DataSyncPeerCandidate>> GetPeersAsync(bool discover, CancellationToken ct);

    /// <summary>Sends /federation/v1/pair/datasync/{request|code}; with TwoWay also offers a reciprocal datasync code (§7.2).</summary>
    Task<DataSyncAccessRequestOutcome> RequestAccessAsync(DataSyncAccessRequestInput input, CancellationToken ct);

    Task<IReadOnlyList<DataSyncAccessRequestView>> GetRequestsAsync(CancellationToken ct);

    /// <summary>Issues the datasync grant; when readBack and the request carried a reciprocal offer, redeems it (§7.2.4).</summary>
    Task<DataSyncApprovalOutcome> ApproveAsync(string requestId, bool readBack, CancellationToken ct);

    Task RejectAsync(string requestId, CancellationToken ct);
    Task CancelOutgoingAsync(string requestId, CancellationToken ct);

    /// <summary>Who may read my definitions.</summary>
    Task<IReadOnlyList<DataSyncGrantView>> GetGrantsAsync(CancellationToken ct);

    Task RevokeAsync(string peerNodeId, CancellationToken ct);
    Task<DataSyncInvitationView> CreateInvitationAsync(DataSyncInvitationInput input, CancellationToken ct);
    Task<bool> HasOutboundGrantAsync(string peerNodeId, CancellationToken ct);

    /// <summary>"Done — stop reading X": drops this device's own datasync credentials for the peer. Never touches library access.</summary>
    Task ForgetOutboundAsync(string peerNodeId, CancellationToken ct);
}

/// <summary>
/// Thrown by <see cref="IDataSyncGrantService"/> for an expected failure on this device that the facade answers as
/// it is: sharing off, remote access off, a request that no longer exists, a wrong or expired code. What a peer
/// answered is a <see cref="DataSyncPeerException"/> instead.
/// </summary>
public sealed class DataSyncProblemException(DataSyncProblem problem)
    : Exception(problem.Detail ?? problem.Code.ToString())
{
    public DataSyncProblem Problem { get; } = problem;
}

public sealed record DataSyncAccessRequestInput(string? PeerNodeId, string? Address, string? Code, DataSyncRequestIntent Intent);

public sealed record DataSyncAccessRequestOutcome(string Outcome /* "granted"|"awaitingApproval"|"rejected" */,
    string? RequestId, string PeerNodeId, string PeerName, string? ReadBack /* "started"|"declined"|null */);

public sealed record DataSyncApprovalOutcome(string PeerNodeId, string PeerName, DataSyncRequestIntent Intent,
    bool ReadBackGranted, string? ReadBackError);

/// <summary>Raised by D's pairing flow, handled by E's scheduler, so links react within seconds (§8.2).</summary>
/// <remarks>
/// For one grant this device issues, the events come in the order things happen: <see cref="InboundGranted"/> first;
/// then, when it announced a read-back, <see cref="OutboundGranted"/> once this device may read the peer, or
/// <see cref="ReadBackFailed"/>.
/// </remarks>
public interface IDataSyncGrantEvents
{
    /// <summary>Our request or code was granted.</summary>
    void OutboundGranted(string peerNodeId);

    /// <summary>We granted a peer.</summary>
    /// <param name="readBackStarted">
    /// The grant is two-way and this device sets out to read the peer back (an approval with ReceiveBack, or a code
    /// made with two-way consent and redeemed two-way): the approver's link is due (§8.1), whether or not that
    /// read-back has finished or will succeed.
    /// </param>
    void InboundGranted(string peerNodeId, DataSyncRequestIntent intent, bool readBackStarted);

    /// <summary>
    /// A read-back announced by <see cref="InboundGranted"/> did not give this device access to the peer, so the
    /// approver's link waits for access with this failure (§7.2.4, N14). <paramref name="errorCode"/> is a
    /// <see cref="DataSyncPeerErrorCode"/> name, or <see cref="DataSyncProblemCode.InvitationInvalid"/> when the peer
    /// refused its own code. The default does nothing, so a handler that predates it still compiles.
    /// </summary>
    void ReadBackFailed(string peerNodeId, string errorCode)
    {
    }
}
