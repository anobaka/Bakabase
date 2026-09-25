using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Transport;
using Bakabase.Service.Controllers;

namespace Bakabase.Service.Components.Federation;

/// <summary>
/// The receiver side of the data sync feed (§7.6): head, manifest and pages of a peer this device holds a
/// <c>datasync.read</c> grant for, over that grant's own session. Every refusal, the peer's or this device's own on
/// the way, reaches data sync as a <see cref="DataSyncPeerException"/> in the words of §7.6.
/// </summary>
/// <remarks>
/// <para>
/// It pulls only from the node it is asked about, and only with a grant this device holds for that node directly
/// (§7.7, D03): nothing here can read one node through another, and a library grant never counts.
/// </para>
/// <para>
/// Head and manifest are shallow and read as the federation envelope; they are checked before data sync sees them.
/// A page is handed over as the raw bytes the source sent, bounded but never parsed here: a deep multilevel property
/// exceeds <c>FederationJson</c>'s depth limit (F61), and <c>DataSyncWireReader</c> reads it.
/// </para>
/// <para>
/// One exchange per peer at a time: a second call for the same peer waits up to <see cref="FetchWait"/>, then gets
/// <see cref="DataSyncPeerErrorCode.Busy"/>. The contract has no member that spans calls, so a whole fetch — head to
/// the last page, which a second manifest would discard at the source — is held by the runtime's own per-peer fetch
/// lock around these calls.
/// </para>
/// </remarks>
public sealed class FederationDataSyncPeerClient(PeerSessionFactory sessions, INodeTransport transport,
    FederationHttpClient http, FederationPeerService peers, TimeProvider timeProvider) : IDataSyncPeerClient
{
    private const string FeedRoute = "/federation/v1/export/datasync";

    /// <summary>What a Retry-After may ask for at most; anything longer reads as a day.</summary>
    private const int MaxRetryAfterSeconds = 24 * 60 * 60;

    /// <summary>The kinds a head or manifest may name, as <c>NodeInfo.DataSyncKinds</c> is budgeted.</summary>
    private const int MaxKinds = 64;

    private const int MaxContractVersion = 1_000_000;
    private const int MaxAppVersionLength = 128;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _peerLocks = new(StringComparer.Ordinal);

    /// <summary>How long a call waits for another call to the same peer before it answers Busy (§7.6).</summary>
    internal TimeSpan FetchWait { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The source waits up to 30 s for its gate before it answers a head (§7.5.1), then refreshes.</summary>
    internal TimeSpan HeadDeadline { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>A manifest builds the whole snapshot at the source, under its gate (§7.5.2).</summary>
    internal TimeSpan ManifestDeadline { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Pages are precomputed; the source never takes its gate for one (§7.5.3).</summary>
    internal TimeSpan PageDeadline { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// What this device knows of a peer and what the peer says about data sync: through the datasync session when
    /// this device may read it, else through its public <c>/info</c>. Never throws for a peer that cannot be
    /// reached; <see cref="DataSyncPeerProbe.ConnectionState"/> says how it went.
    /// </summary>
    /// <exception cref="DataSyncPeerException"><see cref="DataSyncPeerErrorCode.AccessMissing"/> for a node this
    /// device does not know.</exception>
    public async Task<DataSyncPeerProbe> ProbeAsync(string peerNodeId, CancellationToken ct)
    {
        var known = NodeRequestSignature.IsIdentifier(peerNodeId)
            ? (await peers.GetDataSyncStatusAsync(ct)).Peers.FirstOrDefault(p => p.NodeId == peerNodeId)
            : null;
        if (known == null) throw new DataSyncPeerException(DataSyncPeerErrorCode.AccessMissing, "unknownPeer");
        NodeInfo? info = null;
        bool? sharesDefinitions = null;
        string connection;
        if (known.WeMayRead)
        {
            try
            {
                info = (await sessions.GetAsync(peerNodeId, FederationScopes.DataSyncRead, ct)).Info;
            }
            catch (FederationAccessException e) when (IsSharingOff(e.ErrorCode))
            {
                sharesDefinitions = false;
            }
            catch (FederationAccessException)
            {
                // The session's own state says why.
            }
            connection = sessions.GetConnectionState(peerNodeId, FederationScopes.DataSyncRead);
        }
        else if (known.Address != null)
            (info, sharesDefinitions, connection) = await ReadPublicInfoAsync(known.Address, peerNodeId, ct);
        else connection = "Unknown";
        return new DataSyncPeerProbe(peerNodeId, known.Name, known.Address, known.WeMayRead,
            info?.DataSyncContractVersion is >= 0 and <= MaxContractVersion ? info.DataSyncContractVersion : null,
            sharesDefinitions ?? info?.SharesDefinitions, connection);
    }

    public Task<DataSyncFeedHead> GetHeadAsync(string peerNodeId, DataSyncFeedQuery query, CancellationToken ct) =>
        ExchangeAsync(peerNodeId, WithQuery($"{FeedRoute}/head", query), HeadDeadline, async (response, token) =>
        {
            var head = await FederationHttpClient.ReadEnvelopeAsync<DataSyncFeedHead>(response, token);
            return IsValid(head, peerNodeId) ? head : throw Invalid("head");
        }, ct);

    public Task<DataSyncFeedManifest> GetManifestAsync(string peerNodeId, DataSyncFeedQuery query,
        CancellationToken ct) =>
        ExchangeAsync(peerNodeId, WithQuery($"{FeedRoute}/manifest", query), ManifestDeadline,
            async (response, token) =>
            {
                var manifest = await FederationHttpClient.ReadEnvelopeAsync<DataSyncFeedManifest>(response, token);
                return IsValid(manifest, peerNodeId) ? manifest : throw Invalid("manifest");
            }, ct);

    /// <summary>One page's bytes as the source sent them, at most <see cref="FederationHttpClient.MaxControlResponseBytes"/>.</summary>
    public Task<ReadOnlyMemory<byte>> GetPageAsync(string peerNodeId, string snapshotId, string kind, long sinceSeq,
        string? cursor, CancellationToken ct)
    {
        if (!NodeRequestSignature.IsIdentifier(snapshotId)) throw new ArgumentException("Not a snapshot id.", nameof(snapshotId));
        if (!DataSyncFeedQueryString.IsKind(kind)) throw new ArgumentException("Not a kind.", nameof(kind));
        if (!DataSyncFeedQueryString.IsSeq(sinceSeq)) throw new ArgumentOutOfRangeException(nameof(sinceSeq));
        if (cursor != null && !NodeRequestSignature.IsIdentifier(cursor)) throw new ArgumentException("Not a cursor.", nameof(cursor));
        var path = $"{FeedRoute}/changes?snapshot={snapshotId}&kind={kind}&since={sinceSeq}" +
                   (cursor == null ? "" : $"&cursor={cursor}");
        return ExchangeAsync(peerNodeId, path, PageDeadline, async (response, token) =>
            (ReadOnlyMemory<byte>)await FederationHttpClient.ReadBoundedAsync(response.Content,
                FederationHttpClient.MaxControlResponseBytes, token), ct);
    }

    /// <summary>
    /// One signed GET to the peer's feed with its datasync session, under the peer's lock and a deadline; a refusal
    /// of any kind becomes a <see cref="DataSyncPeerException"/>. The caller's own cancellation stays one.
    /// </summary>
    private async Task<T> ExchangeAsync<T>(string peerNodeId, string pathAndQuery, TimeSpan deadline,
        Func<HttpResponseMessage, CancellationToken, Task<T>> read, CancellationToken ct)
    {
        if (!NodeRequestSignature.IsIdentifier(peerNodeId))
            throw new DataSyncPeerException(DataSyncPeerErrorCode.AccessMissing, "unknownPeer");
        var gate = _peerLocks.GetOrAdd(peerNodeId, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(FetchWait, ct))
            throw new DataSyncPeerException(DataSyncPeerErrorCode.Busy, "fetchInProgress");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(deadline);
            try
            {
                for (var attempt = 0;; attempt++)
                {
                    var session = await sessions.GetAsync(peerNodeId, FederationScopes.DataSyncRead, timeout.Token);
                    try
                    {
                        using var response = await transport.SendAsync(session, HttpMethod.Get, pathAndQuery, null,
                            timeout.Token);
                        if (!response.IsSuccessStatusCode) throw await RefusalAsync(response, timeout.Token);
                        return await read(response, timeout.Token);
                    }
                    // The grant or address changed between the session and the request: once more with the current
                    // one, which is refused as it should be when the grant is gone.
                    catch (FederationAccessException e) when (e.ErrorCode == "NodeSessionChanged" && attempt == 0)
                    {
                    }
                }
            }
            catch (FederationAccessException e)
            {
                throw new DataSyncPeerException(Classify(e.ErrorCode, e.StatusCode), e.ErrorCode);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Not the caller: this device's deadline, or its grant for the peer was dropped mid-read.
                throw timeout.IsCancellationRequested
                    ? new DataSyncPeerException(DataSyncPeerErrorCode.Unreachable, "timeout")
                    : new DataSyncPeerException(DataSyncPeerErrorCode.AccessMissing, "accessRemoved");
            }
            catch (Exception e) when (e is HttpRequestException or IOException)
            {
                throw new DataSyncPeerException(DataSyncPeerErrorCode.Unreachable, "connectionLost");
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// A refusal as data sync reads it: the federation error code the peer's body names, else its status, and the
    /// peer's Retry-After. The peer's message is never passed on; the detail is the code alone.
    /// </summary>
    private async Task<DataSyncPeerException> RefusalAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        string? code = null;
        try
        {
            var body = await FederationHttpClient.ReadBoundedAsync(response.Content, 64 * 1024, ct);
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind == JsonValueKind.Object &&
                json.RootElement.TryGetProperty("code", out var wire) && wire.ValueKind == JsonValueKind.String &&
                wire.GetString() is { Length: > 0 and <= 64 } word && word.All(char.IsAsciiLetterOrDigit))
                code = word;
        }
        catch (Exception e) when (e is JsonException or FederationAccessException)
        {
            // A proxy's page or an oversized body: the status says what there is to say.
        }
        return new DataSyncPeerException(Classify(code, status), code ?? $"http{status}",
            RetryAfterSeconds(response, timeProvider.GetUtcNow()));
    }

    /// <summary>
    /// The mapping of §7.6: a federation error code, the peer's or this device's own on the way, as data sync reads
    /// it. A code this build does not know is read by its status.
    /// </summary>
    internal static DataSyncPeerErrorCode Classify(string? code, int status) => code switch
    {
        "NodeUnreachable" => DataSyncPeerErrorCode.Unreachable,
        "NodeNotAuthorized" => DataSyncPeerErrorCode.AccessMissing,
        "GrantRevoked" or "InvalidNodeSignature" or "NodeAuthenticationRequired" or "ScopeNotGranted"
            or "FederationEndpointDenied" => DataSyncPeerErrorCode.AccessRevoked,
        "DataSyncSharingDisabled" or "SharingDisabled" => DataSyncPeerErrorCode.PeerSharingOff,
        "RemoteAccessDisabled" => DataSyncPeerErrorCode.PeerRemoteAccessOff,
        // A route the peer's build does not have: refused by its route policy, or a build whose feed is a stub.
        "NodeRouteForbidden" or "ProtocolUnsupported" or "NotImplemented" => DataSyncPeerErrorCode.PeerTooOld,
        "LibraryEpochChanged" or "IdentityConflict" => DataSyncPeerErrorCode.PeerReset,
        "SourceRestorePending" => DataSyncPeerErrorCode.PeerRestorePending,
        "SnapshotExpired" or "SnapshotMismatch" => DataSyncPeerErrorCode.SnapshotExpired,
        "CursorSuperseded" => DataSyncPeerErrorCode.CursorSuperseded,
        // A signature outside the clock window or replayed is this exchange's timing, not access: try again soon.
        "Busy" or "TooManySnapshots" or "SignatureExpired" or "SignatureReplayed" => DataSyncPeerErrorCode.Busy,
        "NodeResponseTooLarge" or "SnapshotTooLarge" => DataSyncPeerErrorCode.TooLarge,
        // The session changed twice in a row under this exchange: the next attempt reads the settled state.
        "NodeSessionChanged" => DataSyncPeerErrorCode.Busy,
        "InvalidAddress" => DataSyncPeerErrorCode.Unreachable,
        "InvalidNodeResponse" or "NodeRedirectRefused" or "InvalidNodeRoute" or "UnknownKind" or "InvalidFeedQuery" =>
            DataSyncPeerErrorCode.InvalidResponse,
        _ => status switch
        {
            401 or 403 => DataSyncPeerErrorCode.AccessRevoked,
            404 or 501 => DataSyncPeerErrorCode.PeerTooOld,
            410 => DataSyncPeerErrorCode.SnapshotExpired,
            413 => DataSyncPeerErrorCode.TooLarge,
            429 => DataSyncPeerErrorCode.Busy,
            502 or 503 or 504 => DataSyncPeerErrorCode.Unreachable,
            _ => DataSyncPeerErrorCode.InvalidResponse
        }
    };

    /// <summary>The peer's Retry-After in seconds, as a delay or a date; at most a day, never negative.</summary>
    internal static int? RetryAfterSeconds(HttpResponseMessage response, DateTimeOffset now)
    {
        var header = response.Headers.RetryAfter;
        var delay = header?.Delta ?? (header?.Date is { } date ? date - now : null);
        return delay is { } value
            ? (int)Math.Clamp(Math.Ceiling(value.TotalSeconds), 0, MaxRetryAfterSeconds)
            : null;
    }

    /// <summary>
    /// What a reader declares with head and manifest (§7.5): values the source's own checks accept, never escaped,
    /// since each is made of characters a query carries as they are.
    /// </summary>
    internal static string QueryString(DataSyncFeedQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var since = query.Since ?? new Dictionary<string, long>();
        if (query.Mode != null && query.Mode is not ("follow" or "twoWay"))
            throw new ArgumentException("The mode is follow or twoWay.", nameof(query));
        if (since.Count > DataSyncFeedQueryString.MaxSincePairs ||
            since.Any(p => !DataSyncFeedQueryString.IsKind(p.Key) || !DataSyncFeedQueryString.IsSeq(p.Value)))
            throw new ArgumentException("The cursors are at most 16 kinds with a sequence each.", nameof(query));
        if (query.ReaderActorId != null && !DataSyncActorId.IsValid(query.ReaderActorId))
            throw new ArgumentException("Not an actor id.", nameof(query));
        if (query.ReaderState != null && (query.ReaderState.Length is 0 or > 64 ||
                                          !query.ReaderState.All(c => char.IsAsciiLetterOrDigit(c) || c == ':')))
            throw new ArgumentException("The state is at most 64 letters, digits and colons.", nameof(query));
        var parts = new List<string>(4);
        if (query.Mode != null) parts.Add("mode=" + query.Mode);
        if (since.Count > 0)
            parts.Add("since=" + string.Join(',', since.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => $"{p.Key}:{p.Value.ToString(CultureInfo.InvariantCulture)}")));
        if (query.ReaderActorId != null) parts.Add("actor=" + query.ReaderActorId);
        if (query.ReaderState != null) parts.Add("state=" + query.ReaderState);
        return string.Join('&', parts);
    }

    private static string WithQuery(string path, DataSyncFeedQuery query) =>
        QueryString(query) is { Length: > 0 } values ? path + "?" + values : path;

    /// <summary>A head this device can act on: from the node asked, within the protocol's budgets.</summary>
    internal static bool IsValid(DataSyncFeedHead? head, string peerNodeId) =>
        head != null && IsSource(head.NodeId, head.LibraryEpoch, head.ActorId, head.ContractVersion,
            head.MinimumPeerContract, head.AppVersion, peerNodeId) &&
        DataSyncFeedQueryString.IsSeq(head.Seq) &&
        head.SeenCounter is null or >= 0 and <= DataSyncFeedQueryString.MaxSeq &&
        head.Kinds is { Count: <= MaxKinds } kinds && kinds.All(k => k != null && DataSyncFeedQueryString.IsKind(k.Kind) &&
            k.SchemaVersion is >= 0 and <= MaxContractVersion && DataSyncFeedQueryString.IsSeq(k.MaxSeq) &&
            k.ComparisonFormVersion is >= 0 and <= MaxContractVersion) &&
        Distinct(kinds.Select(k => k.Kind)) && IsValid(head.Attention) && IsValid(head.Counterpart);

    /// <summary>A manifest this device can read pages of: its snapshot id, and per kind counts and a content hash.</summary>
    internal static bool IsValid(DataSyncFeedManifest? manifest, string peerNodeId) =>
        manifest != null && NodeRequestSignature.IsIdentifier(manifest.SnapshotId) &&
        manifest.ExpiresInMs is >= 0 and <= 24L * 60 * 60 * 1000 &&
        IsSource(manifest.NodeId, manifest.LibraryEpoch, manifest.ActorId, manifest.ContractVersion,
            manifest.MinimumPeerContract, manifest.AppVersion, peerNodeId) &&
        manifest.Kinds is { Count: <= MaxKinds } kinds && kinds.All(k => k != null &&
            DataSyncFeedQueryString.IsKind(k.Kind) && k.SchemaVersion is >= 0 and <= MaxContractVersion &&
            DataSyncFeedQueryString.IsSeq(k.MaxSeq) && DataSyncFeedQueryString.IsSeq(k.TombstoneFloorSeq) &&
            DataSyncFeedQueryString.IsSeq(k.SinceSeq) && k.LiveCount >= 0 && k.TombstoneCount >= 0 &&
            k.RecordCount >= 0 && ContentHash.IsValid(k.ContentHash)) &&
        Distinct(kinds.Select(k => k.Kind)) && IsValid(manifest.Attention) && IsValid(manifest.Counterpart);

    /// <summary>
    /// The source's own identity: the node asked, and never another one's feed (§7.7) — its session proved that id,
    /// and a head or manifest naming another is refused rather than read as that node's.
    /// </summary>
    private static bool IsSource(string? nodeId, string? libraryEpoch, string? actorId, int contract, int minimum,
        string? appVersion, string peerNodeId) =>
        nodeId == peerNodeId && NodeRequestSignature.IsIdentifier(libraryEpoch) && DataSyncActorId.IsValid(actorId) &&
        contract is >= 0 and <= MaxContractVersion && minimum is >= 0 and <= MaxContractVersion &&
        appVersion is { Length: <= MaxAppVersionLength } && !appVersion.Any(char.IsControl);

    private static bool IsValid(DataSyncSourceAttention? attention) => attention is
        { OpenDecisions: >= 0, PausedLinks: >= 0, AwaitingReview: >= 0 };

    private static bool IsValid(DataSyncFeedCounterpart? counterpart) => counterpart == null ||
        counterpart.Mode is "off" or "follow" or "twoWay" &&
        counterpart.Kinds is { Count: <= MaxKinds } kinds && kinds.All(DataSyncFeedQueryString.IsKind) && Distinct(kinds);

    private static bool Distinct(IEnumerable<string> kinds)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return kinds.All(seen.Add);
    }

    private static DataSyncPeerException Invalid(string what) => new(DataSyncPeerErrorCode.InvalidResponse, what);

    private static bool IsSharingOff(string code) => code is "SharingDisabled" or "DataSyncSharingDisabled";

    /// <summary>
    /// A peer this device may not read yet, through the <c>/info</c> anyone may read: what it says about data sync
    /// when it answers as that node. <c>info</c> refused because nothing is shared still says the device is there.
    /// </summary>
    private async Task<(NodeInfo? Info, bool? SharesDefinitions, string Connection)> ReadPublicInfoAsync(
        string address, string peerNodeId, CancellationToken ct)
    {
        try
        {
            var info = await http.PublicAsync<NodeInfo>(address, HttpMethod.Get, "/federation/v1/info", null, ct);
            return info.NodeId == peerNodeId ? (info, null, "Online") : (null, null, "IdentityConflict");
        }
        catch (FederationAccessException e) when (IsSharingOff(e.ErrorCode))
        {
            return (null, false, "Online");
        }
        catch (FederationAccessException e)
        {
            return (null, null, e.StatusCode is 401 or 403 ? "Unauthorized" : "Offline");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (null, null, "Offline");
        }
    }
}
