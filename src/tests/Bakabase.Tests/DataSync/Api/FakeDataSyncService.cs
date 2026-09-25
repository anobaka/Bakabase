using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Tests.DataSync.Api;

/// <summary>
/// The data sync facade with canned answers, for API tests and as the reference of what the page will be shown: a
/// link in every <see cref="DataSyncLinkState"/>, peers with every kind of source attention, requests both ways, an
/// inbox item of every <see cref="DataSyncInboxItemType"/> with the origin §9.1 gives it (both identity conflict rows,
/// and a closed item), a staged review whose plan has an item of every <see cref="DataSyncPlanItemType"/>, history,
/// an undo preview and a pending restore. Every call is recorded in <see cref="Calls"/>.
/// </summary>
/// <remarks>
/// Every time is UTC, as the real service answers (spec §2.10). Mutations answer as the real service would on
/// success, and with the matching problem for an id the canned data does not have; they never change the data.
/// </remarks>
public sealed class FakeDataSyncService : IDataSyncService
{
    public static readonly DateTime Now = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    public const string ReviewId = "review-1";
    public const string PlanId = "0123456789abcdef";
    public const string BackupPath = "/data/backups";
    public const long TypeChangeItemId = 3;

    private static readonly string[] AllKinds = [..DataSyncKindIds.All];

    private readonly List<string> _calls = [];

    /// <summary>The names of the <see cref="IDataSyncService"/> members called, in order.</summary>
    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.._calls];
            }
        }
    }

    public DataSyncOverview Overview { get; set; } = CannedOverview();
    public IReadOnlyList<DataSyncLinkView> Links { get; set; } = CannedLinks();
    public IReadOnlyList<DataSyncPeerCandidate> Peers { get; set; } = CannedPeers();
    public IReadOnlyList<DataSyncAccessRequestView> Requests { get; set; } = CannedRequests();
    public IReadOnlyList<DataSyncReaderView> Readers { get; set; } = CannedReaders();
    public IReadOnlyList<DataSyncInboxItemView> Inbox { get; set; } = CannedInbox();
    public DataSyncReviewResult Review { get; set; } = CannedReview();
    public IReadOnlyList<DataSyncEntityStatusView> Entities { get; set; } = CannedEntities();
    public IReadOnlyList<DataSyncHistoryEntry> History { get; set; } = CannedHistory();
    public DataSyncRestoreView Restore { get; set; } = CannedRestore();

    public Task<DataSyncOverview> GetOverviewAsync(CancellationToken ct) => Answer(Overview);

    public Task<DataSyncMapView> GetMapAsync(CancellationToken ct) =>
        Answer(new DataSyncMapView(Overview.SharingEnabled, Overview.RemoteAccessMode,
            Links.Select(ToMapPeer).ToList(),
            Requests.Where(r => r.Direction == DataSyncRequestDirection.Incoming)
                .Select(r => new DataSyncMapRequest(r.RequestId, r.NodeId, r.NodeName, r.RemoteAddress, r.Intent,
                    r.ExpiresAt, r.ClaimsKnownDevice, r.KnownAddress))
                .ToList(),
            [
                new DataSyncMapOutgoing(2, "node-pc2", "PC-2", "192.168.1.20:34567", DataSyncLinkState.AwaitingAccess,
                    "awaitingApproval", Now.AddHours(1)),
                new DataSyncMapOutgoing(6, "node-old", "Old laptop", "192.168.1.60:34567", DataSyncLinkState.Stopped,
                    "rejected", null),
                new DataSyncMapOutgoing(12, "node-away", "Away PC", "192.168.1.120:34567", DataSyncLinkState.Stopped,
                    "expired", Now.AddDays(-1)),
            ]));

    public Task<DataSyncProblem?> SetSharingAsync(DataSyncSharingInput input, CancellationToken ct) => Ok();

    public Task<IReadOnlyList<DataSyncPeerCandidate>> GetPeersAsync(bool discover, CancellationToken ct) =>
        Answer(discover ? Peers : Peers.Where(p => p.Known).ToList());

    public Task<IReadOnlyList<DataSyncLinkView>> GetLinksAsync(CancellationToken ct) => Answer(Links);

    /// <remarks>
    /// Sends a request (or claims a code) unless this device already reads the peer, and offers a reciprocal code for a
    /// two-way link the peer does not read back yet; either needs a caller that may create access (§7.1.5).
    /// </remarks>
    public Task<DataSyncLinkResult> CreateLinkAsync(DataSyncLinkCreateInput input, bool callerMayCreateAccess,
        CancellationToken ct)
    {
        var peer = Peers.FirstOrDefault(p => p.NodeId == input.PeerNodeId);
        var sendsRequest = peer is not {WeMayRead: true};
        var mintsReciprocalCode = input.Mode == DataSyncLinkMode.TwoWay && peer is not {TheyMayRead: true};
        if ((sendsRequest || mintsReciprocalCode) && !callerMayCreateAccess)
        {
            return Answer(NotAllowedLink);
        }

        return Answer(new DataSyncLinkResult(
            Link(20, input.PeerNodeId ?? "node-new", peer?.Name ?? "New PC",
                sendsRequest ? DataSyncLinkState.AwaitingAccess : DataSyncLinkState.AwaitingReview, input.Mode,
                peerAddress: input.Address, peerMayReadUs: peer is {TheyMayRead: true}),
            sendsRequest ? "req-out-2" : null, null, null));
    }

    /// <remarks>
    /// Turning a link two-way offers a reciprocal code when the peer does not read this device yet (§7.2.4); turning a
    /// stopped link back on, following, or changing kinds never creates access.
    /// </remarks>
    public Task<DataSyncLinkResult> UpdateLinkAsync(int linkId, DataSyncLinkUpdateInput input,
        bool callerMayCreateAccess, CancellationToken ct)
    {
        if (FindLink(linkId) is not { } link)
        {
            return Answer(LinkNotFound);
        }

        if (input.Mode == DataSyncLinkMode.TwoWay && !link.PeerMayReadUs && !callerMayCreateAccess)
        {
            return Answer(NotAllowedLink);
        }

        return Answer(new DataSyncLinkResult(link with
        {
            Mode = input.Mode ?? link.Mode, Kinds = input.Kinds ?? link.Kinds
        }, null, null, null));
    }

    public Task<DataSyncLinkResult> PauseLinkAsync(int linkId, CancellationToken ct) =>
        Answer(FindLink(linkId) is { } link
            ? new DataSyncLinkResult(link with
            {
                State = DataSyncLinkState.Paused, PausedReason = DataSyncPauseReason.ByUser
            }, null, null, null)
            : LinkNotFound);

    public Task<DataSyncLinkResult> ResumeLinkAsync(int linkId, DataSyncResumeAction action, CancellationToken ct) =>
        Answer(FindLink(linkId) is { } link
            ? new DataSyncLinkResult(link with {State = DataSyncLinkState.Active, PausedReason = null},
                action == DataSyncResumeAction.AskAccessAgain ? "req-out-3" : null, null, null)
            : LinkNotFound);

    public Task<DataSyncTaskStart> SyncNowAsync(int? linkId, CancellationToken ct) =>
        Answer(linkId == null || FindLink(linkId.Value) != null
            ? new DataSyncTaskStart("DataSync", null)
            : new DataSyncTaskStart(null, Problem(DataSyncProblemCode.LinkNotFound)));

    public Task<DataSyncProblem?> ResetLinkAsync(int linkId, CancellationToken ct) =>
        Answer(FindLink(linkId) == null ? Problem(DataSyncProblemCode.LinkNotFound) : null);

    public Task<DataSyncProblem?> SetAllPausedAsync(bool paused, CancellationToken ct) => Ok();

    public Task<DataSyncProblem?> ForgetAccessAsync(string peerNodeId, CancellationToken ct) => Ok();

    /// <remarks>Sends a request (or claims a code) unless this device already reads the peer (§7.1.5).</remarks>
    public Task<DataSyncReviewResult> CreateCopyOnceAsync(DataSyncCopyOnceInput input, bool callerMayCreateAccess,
        CancellationToken ct) =>
        Answer(Peers.Any(p => p.NodeId == input.PeerNodeId && p.WeMayRead) || callerMayCreateAccess
            ? new DataSyncReviewResult(null, 21, true, DataSyncLinkMode.Off, null, null, null, null, null, null, null)
            : new DataSyncReviewResult(null, null, true, DataSyncLinkMode.Off, null, null, null, null, null, null,
                Problem(DataSyncProblemCode.NotAllowedOnThisDevice)));

    public Task<DataSyncReviewResult> GetReviewAsync(string reviewId, CancellationToken ct) =>
        Answer(reviewId == ReviewId ? Review : ReviewExpired);

    public Task<DataSyncReviewResult> RefetchReviewAsync(string reviewId, CancellationToken ct) =>
        Answer(reviewId == ReviewId ? Review : ReviewExpired);

    public Task<DataSyncChangePage> GetReviewChangesAsync(string reviewId, string planId, string itemId,
        string? candidateLocalKey, int skip, int take, CancellationToken ct)
    {
        if (reviewId != ReviewId)
        {
            return Answer(new DataSyncChangePage(planId, [], [], 0, Problem(DataSyncProblemCode.ReviewExpired)));
        }

        if (planId != PlanId)
        {
            return Answer(new DataSyncChangePage(planId, [], [], 0, Problem(DataSyncProblemCode.PlanChanged)));
        }

        var changes = Enumerable.Range(0, 240)
            .Select(i => new DataSyncFieldChange($"add:{i}", DataSyncFieldChangeKind.AddChild, "tags", null,
                new DataSyncDisplayValue($"Tag {i}", Group: "Genre"), null, null))
            .ToList();
        return Answer(new DataSyncChangePage(planId, changes.Skip(skip).Take(take).ToList(), [], changes.Count, null));
    }

    public Task<DataSyncApplyStart> ApplyReviewAsync(string reviewId, DataSyncReviewApplyInput input,
        CancellationToken ct) =>
        Answer(reviewId == ReviewId
            ? new DataSyncApplyStart($"DataSyncApply:{reviewId}", null, [], null)
            : new DataSyncApplyStart(null, Problem(DataSyncProblemCode.ReviewExpired), [], null));

    public Task<DataSyncReviewCancelResult> CancelReviewApplyAsync(string reviewId, CancellationToken ct) =>
        Answer(reviewId == ReviewId
            ? new DataSyncReviewCancelResult(DataSyncReviewState.Staged, null)
            : new DataSyncReviewCancelResult(null, Problem(DataSyncProblemCode.ReviewExpired)));

    public Task DiscardReviewAsync(string reviewId, CancellationToken ct)
    {
        Record();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DataSyncAccessRequestView>> GetRequestsAsync(CancellationToken ct) => Answer(Requests);

    public Task<DataSyncRequestResult> ApproveRequestAsync(string requestId, DataSyncApproveInput input,
        CancellationToken ct)
    {
        var request = Requests.FirstOrDefault(r =>
            r.RequestId == requestId && r.Direction == DataSyncRequestDirection.Incoming);
        if (request == null)
        {
            return Answer(new DataSyncRequestResult(null, false, Problem(DataSyncProblemCode.RequestNotFound)));
        }

        var twoWay = request.Intent == DataSyncRequestIntent.TwoWay && input.ReceiveBack;
        return Answer(new DataSyncRequestResult(
            twoWay
                ? Link(22, request.NodeId, request.NodeName, DataSyncLinkState.WaitingForPeerReview,
                    DataSyncLinkMode.TwoWay, initiator: DataSyncLinkInitiator.Peer)
                : null,
            twoWay, null));
    }

    public Task<DataSyncProblem?> RejectRequestAsync(string requestId, CancellationToken ct) =>
        Answer(Requests.Any(r => r.RequestId == requestId) ? null : Problem(DataSyncProblemCode.RequestNotFound));

    public Task<DataSyncProblem?> CancelRequestAsync(string requestId, CancellationToken ct) =>
        Answer(Requests.Any(r => r.RequestId == requestId) ? null : Problem(DataSyncProblemCode.RequestNotFound));

    public Task<IReadOnlyList<DataSyncReaderView>> GetReadersAsync(CancellationToken ct) => Answer(Readers);

    public Task<DataSyncProblem?> RevokeReaderAsync(string peerNodeId, CancellationToken ct) => Ok();

    public Task<DataSyncInvitationResult> CreateInvitationAsync(DataSyncInvitationInput input, CancellationToken ct) =>
        Answer(new DataSyncInvitationResult(
            new DataSyncInvitationView("48213705", Now.AddMinutes(10), ["http://192.168.1.10:34567"],
                input.AllowTwoWay), null));

    public Task<DataSyncInboxPage> GetInboxAsync(DataSyncInboxQuery query, CancellationToken ct)
    {
        var matching = Inbox
            .Where(i => !query.OpenOnly || i.ClosedAt == null)
            .Where(i => query.PeerNodeId == null || i.PeerNodeId == query.PeerNodeId)
            .Where(i => query.Kind == null || i.Kind == query.Kind)
            .ToList();
        return Answer(new DataSyncInboxPage(matching.Skip(query.Skip).Take(query.Take).ToList(), matching.Count,
            Inbox.Count(i => i.ClosedAt == null)));
    }

    public Task<DataSyncInboxItemView?> GetInboxItemAsync(long id, CancellationToken ct) =>
        Answer(Inbox.FirstOrDefault(i => i.Id == id));

    public Task<DataSyncTypeChangePreview?> PreviewInboxItemAsync(long id, CancellationToken ct) =>
        Answer(Inbox.FirstOrDefault(i => i.Id == id)?.Type == DataSyncInboxItemType.TypeChange
            ? new DataSyncTypeChangePreview("MultipleChoice", "SingleChoice", 1234, 1234, 210,
            [
                new DataSyncTypeChangeSample("Action, Comedy", "Action"),
                new DataSyncTypeChangeSample("Horror", "Horror"),
            ])
            : null);

    public Task<DataSyncTaskStart> ResolveAsync(DataSyncResolveBatchInput input, CancellationToken ct)
    {
        if (input.Items.Count == 0)
        {
            return Answer(new DataSyncTaskStart(null, Problem(DataSyncProblemCode.NothingSelected)));
        }

        return Answer(input.Items.All(r => Inbox.Any(i => i.Id == r.ItemId && i.ClosedAt == null))
            ? new DataSyncTaskStart("DataSyncResolve:batch-1", null)
            : new DataSyncTaskStart(null, Problem(DataSyncProblemCode.UnknownItem)));
    }

    public Task<IReadOnlyList<DataSyncEntityStatusView>> GetEntitiesAsync(string kind, CancellationToken ct) =>
        Answer<IReadOnlyList<DataSyncEntityStatusView>>(Entities.Where(e => KindOf(e) == kind).ToList());

    public Task<DataSyncTaskStart> SetEntitySyncAsync(string kind, string localKey, DataSyncEntitySyncInput input,
        CancellationToken ct) =>
        Answer(DataSyncKindIds.All.Contains(kind)
            ? new DataSyncTaskStart(null, null)
            : new DataSyncTaskStart(null, Problem(DataSyncProblemCode.UnknownKind)));

    public Task<IReadOnlyList<DataSyncHistoryEntry>> GetHistoryAsync(CancellationToken ct) => Answer(History);

    public Task<DataSyncHistoryDetail?> GetHistoryEntryAsync(int id, CancellationToken ct) =>
        Answer(History.FirstOrDefault(h => h.Id == id) is { } entry
            ? new DataSyncHistoryDetail(entry,
            [
                new DataSyncHistoryItem("customProperty/k/0f1e2d3c4b5a69788796a5b4c3d2e1f0", DataSyncKindIds.CustomProperty,
                    "Genre", DataSyncItemOutcome.Applied, DataSyncItemAction.Updated, "12",
                    DataSyncPlanItemType.Update),
                new DataSyncHistoryItem("customProperty/k/1f1e2d3c4b5a69788796a5b4c3d2e1f0", DataSyncKindIds.CustomProperty,
                    "Rating", DataSyncItemOutcome.Applied, DataSyncItemAction.Created, "13",
                    DataSyncPlanItemType.Create),
                new DataSyncHistoryItem("extensionGroup/k/2f1e2d3c4b5a69788796a5b4c3d2e1f0", DataSyncKindIds.ExtensionGroup,
                    "Video", DataSyncItemOutcome.Held, DataSyncItemAction.None, null, DataSyncPlanItemType.Held),
            ])
            : null);

    public Task<DataSyncUndoPreview> PreviewUndoAsync(int id, CancellationToken ct) =>
        Answer(History.Any(h => h.Id == id)
            ? new DataSyncUndoPreview(true,
            [
                new DataSyncUndoPreviewItem(DataSyncKindIds.CustomProperty, "13", "Rating", DataSyncUndoAction.Remove,
                    null, 0, true, false),
                new DataSyncUndoPreviewItem(DataSyncKindIds.CustomProperty, "12", "Genre", DataSyncUndoAction.Revert,
                    DataSyncUndoBlock.ChangedSinceImport, 412, false, false),
                new DataSyncUndoPreviewItem(DataSyncKindIds.CustomProperty, "14", "Mood", DataSyncUndoAction.Recreate,
                    null, null, false, true),
            ], null, BackupPath)
            : new DataSyncUndoPreview(false, [], Problem(DataSyncProblemCode.UndoNotAvailable), BackupPath));

    public Task<DataSyncTaskStart> StartUndoAsync(int id, CancellationToken ct) =>
        Answer(History.Any(h => h.Id == id)
            ? new DataSyncTaskStart($"DataSyncUndo:{id}", null)
            : new DataSyncTaskStart(null, Problem(DataSyncProblemCode.UndoNotAvailable)));

    public Task<DataSyncRestoreView> GetRestoreAsync(CancellationToken ct) => Answer(Restore);

    public Task<DataSyncTaskStart> ChooseRestoreAsync(DataSyncRestoreChoice choice, int? linkId, CancellationToken ct) =>
        Answer(new DataSyncTaskStart($"DataSyncRestore:{choice}", null));

    public Task<DataSyncProblem?> CancelTaskAsync(string taskId, CancellationToken ct) => Ok();

    private Task<T> Answer<T>(T value, [System.Runtime.CompilerServices.CallerMemberName] string member = "")
    {
        Record(member);
        return Task.FromResult(value);
    }

    private Task<DataSyncProblem?> Ok([System.Runtime.CompilerServices.CallerMemberName] string member = "") =>
        Answer<DataSyncProblem?>(null, member);

    private void Record([System.Runtime.CompilerServices.CallerMemberName] string member = "")
    {
        lock (_calls)
        {
            _calls.Add(member);
        }
    }

    private DataSyncLinkView? FindLink(int id) => Links.FirstOrDefault(l => l.Id == id);

    private static DataSyncProblem Problem(DataSyncProblemCode code) => new(code, null);

    private static readonly DataSyncLinkResult LinkNotFound =
        new(null, null, null, Problem(DataSyncProblemCode.LinkNotFound));

    private static readonly DataSyncLinkResult NotAllowedLink =
        new(null, null, null, Problem(DataSyncProblemCode.NotAllowedOnThisDevice));

    private static readonly DataSyncReviewResult ReviewExpired = new(null, null, false, DataSyncLinkMode.Off, null,
        null, null, null, null, null, Problem(DataSyncProblemCode.ReviewExpired));

    private static string KindOf(DataSyncEntityStatusView entity) =>
        entity.LocalKey.StartsWith("ext-", StringComparison.Ordinal)
            ? DataSyncKindIds.ExtensionGroup
            : DataSyncKindIds.CustomProperty;

    private static DataSyncMapPeer ToMapPeer(DataSyncLinkView link) =>
        new(link.PeerNodeId, link.PeerName, link.Id, link.Mode, link.LastMode, link.State, link.PausedReason,
            link.Mode != DataSyncLinkMode.Off && link.State == DataSyncLinkState.Active,
            link.State is DataSyncLinkState.AwaitingAccess or DataSyncLinkState.AwaitingReview
                or DataSyncLinkState.WaitingForPeerReview,
            link.PeerMayReadUs, link.PeerModeTowardsUs, link.PeerKinds, link.PeerLastReadAt, link.LastSyncedAt,
            link.OpenItems, link.PeerAttention, link.ReadBackDeclined, link.LastErrorCode, link.Kinds);

    // ---- canned data ---------------------------------------------------------------------------------------------

    private static DataSyncOverview CannedOverview() =>
        new("This PC", "node-self", false, true, RemoteAccessMode.Enabled, true, false, false,
            [new DataSyncKindCount(DataSyncKindIds.ExtensionGroup, 8), new DataSyncKindCount(DataSyncKindIds.CustomProperty, 100)],
            new DataSyncStatusView(DataSyncStatusLevel.NeedsYou, 12, 12, 1, 1, Now.AddMinutes(-5), null),
            null, true, 12, 2, 52_428_800, BackupPath, ["http://192.168.1.10:34567"]);

    private static DataSyncLinkView Link(int id, string peerNodeId, string peerName, DataSyncLinkState state,
        DataSyncLinkMode mode, DataSyncLinkMode? lastMode = null, DataSyncPauseReason? pausedReason = null,
        DataSyncLinkInitiator initiator = DataSyncLinkInitiator.ThisDevice, string? peerAddress = null,
        string? lastErrorCode = null, int openItems = 0, string? reviewId = null, int? peerContractVersion = 1,
        bool peerMayReadUs = false, string? peerModeTowardsUs = null, DataSyncSourceAttention? attention = null,
        bool peerOnline = true, bool synced = false) =>
        new(id, peerNodeId, peerName, peerAddress ?? $"192.168.1.{id * 10}:34567", mode, lastMode ?? mode, state,
            pausedReason, null, initiator, AllKinds, synced ? AllKinds : null, synced ? Now.AddMinutes(-5) : null,
            state == DataSyncLinkState.Active ? Now.AddMinutes(1) : null, lastErrorCode, null, openItems, 0, reviewId,
            peerContractVersion == null ? null : "2.5.0-beta.10", peerContractVersion, peerMayReadUs, false,
            peerModeTowardsUs, peerMayReadUs ? Now.AddMinutes(-6) : null, attention, 0, synced ? 1 : 0, 0,
            peerOnline);

    private static IReadOnlyList<DataSyncLinkView> CannedLinks() =>
    [
        // A headless hub in step, holding back two decisions nobody has taken there (§9.1 N).
        Link(1, "node-nas", "NAS", DataSyncLinkState.Active, DataSyncLinkMode.TwoWay, openItems: 9,
            peerMayReadUs: true, peerModeTowardsUs: "twoWay", attention: new DataSyncSourceAttention(true, 2, 0, false, 0),
            synced: true),
        Link(2, "node-pc2", "PC-2", DataSyncLinkState.AwaitingAccess, DataSyncLinkMode.Follow,
            peerAddress: "192.168.1.20:34567", peerContractVersion: null),
        Link(3, "node-laptop", "Laptop", DataSyncLinkState.AwaitingReview, DataSyncLinkMode.TwoWay,
            reviewId: ReviewId, peerMayReadUs: true, attention: new DataSyncSourceAttention(false, 0, 0, false, 0)),
        Link(4, "node-studio", "Studio", DataSyncLinkState.WaitingForPeerReview, DataSyncLinkMode.TwoWay,
            initiator: DataSyncLinkInitiator.Peer, peerMayReadUs: true, peerModeTowardsUs: "twoWay",
            attention: new DataSyncSourceAttention(false, 0, 0, false, 1)),
        Link(5, "node-htpc", "HTPC", DataSyncLinkState.Paused, DataSyncLinkMode.Follow,
            pausedReason: DataSyncPauseReason.ByUser, attention: new DataSyncSourceAttention(false, 0, 1, true, 0),
            synced: true),
        Link(6, "node-old", "Old laptop", DataSyncLinkState.Stopped, DataSyncLinkMode.Off,
            lastMode: DataSyncLinkMode.Follow, lastErrorCode: "AccessRejected", peerOnline: false),
        Link(7, "node-legacy", "Legacy NAS", DataSyncLinkState.PeerTooOld, DataSyncLinkMode.Follow,
            lastErrorCode: "PeerTooOld", peerContractVersion: 0),
        Link(8, "node-beta", "Beta PC", DataSyncLinkState.ThisTooOld, DataSyncLinkMode.TwoWay,
            lastErrorCode: "ThisTooOld", peerContractVersion: 2),
        Link(9, "node-office", "Office PC", DataSyncLinkState.AccessRevoked, DataSyncLinkMode.Follow,
            lastErrorCode: "AccessRevoked", synced: true),
        Link(10, "node-tablet", "Tablet PC", DataSyncLinkState.PeerSharingOff, DataSyncLinkMode.Follow,
            lastErrorCode: "PeerSharingOff", synced: true),
        Link(11, "node-mini", "Mini PC", DataSyncLinkState.PeerRemoteAccessOff, DataSyncLinkMode.TwoWay,
            lastErrorCode: "PeerRemoteAccessOff", peerMayReadUs: true, synced: true),
        Link(12, "node-away", "Away PC", DataSyncLinkState.Stopped, DataSyncLinkMode.Off,
            lastMode: DataSyncLinkMode.TwoWay, lastErrorCode: "AccessExpired", peerOnline: false),
    ];

    private static IReadOnlyList<DataSyncPeerCandidate> CannedPeers() =>
    [
        new("node-nas", "NAS", "192.168.1.10:34567", true, true, 1, true, true, true, 1, "Online"),
        new("node-pc2", "PC-2", "192.168.1.20:34567", true, false, null, null, false, false, 2, "Unknown"),
        new("node-newpc", "New PC", "192.168.1.40:34567", false, true, 1, true, false, false, null, null),
        new("node-library-only", "Library PC", "192.168.1.50:34567", true, true, 1, false, false, false, null,
            "Online"),
    ];

    private static IReadOnlyList<DataSyncAccessRequestView> CannedRequests() =>
    [
        new("req-in-1", DataSyncRequestDirection.Incoming, "node-newpc", "New PC", DataSyncRequestIntent.TwoWay,
            "pending", Now.AddMinutes(30), "192.168.1.40", false, null, false),
        // A claim naming a device this one knows, from another address (M5).
        new("req-in-2", DataSyncRequestDirection.Incoming, "node-nas", "NAS", DataSyncRequestIntent.Follow, "pending",
            Now.AddMinutes(45), "192.168.1.99", true, "192.168.1.10:34567", true),
        new("req-out-1", DataSyncRequestDirection.Outgoing, "node-pc2", "PC-2", DataSyncRequestIntent.Follow,
            "pending", Now.AddHours(1), "192.168.1.20:34567", false, null, false),
    ];

    private static IReadOnlyList<DataSyncReaderView> CannedReaders() =>
    [
        new("node-nas", "NAS", Now.AddDays(-30), Now.AddMinutes(-6), "twoWay", "needsYou:2", true),
        new("node-studio", "Studio", Now.AddHours(-2), null, "twoWay", "awaitingReview", false),
        new("node-mini", "Mini PC", Now.AddDays(-3), Now.AddDays(-1), "twoWay", "paused:byUser", false),
    ];

    private static DataSyncInboxPayload Payload(string entityName, string? subtype = "MultipleChoice",
        IReadOnlyList<DataSyncFieldOutcome>? fields = null, int? valueCount = null, int? usageCount = null,
        IReadOnlyList<DataSyncDisplayValue>? children = null, int childrenTotal = 0, string? remoteSubtype = null,
        string? localSubtype = null, IReadOnlyList<DataSyncInboxCandidate>? candidates = null,
        IReadOnlyList<DataSyncInboxRecordRef>? records = null, IReadOnlyList<DataSyncLargeChangeEntry>? largeChange = null,
        string? peerName = "NAS") =>
        new(entityName, subtype, peerName,
            peerName == null ? null : new DataSyncEditorRef("node-nas", peerName, "actor-nas-1"), "This PC",
            fields ?? [], valueCount, usageCount, children, childrenTotal, remoteSubtype, localSubtype, candidates,
            records, largeChange);

    private static DataSyncInboxItemView Item(long id, DataSyncInboxItemType type, DataSyncInboxItemOrigin origin,
        string subjectPath, DataSyncInboxPayload payload, IReadOnlyList<DataSyncInboxAction> actions,
        string? localKey = "12", int? linkId = 1, string kind = DataSyncKindIds.CustomProperty) =>
        new(id, linkId, linkId == null ? null : "node-nas", linkId == null ? null : "NAS", kind, localKey, type, origin,
            subjectPath, payload, actions, null, $"{id:x32}", Now.AddHours(-id), Now.AddMinutes(-id), null, null, null,
            null);

    private static IReadOnlyList<DataSyncInboxItemView> CannedInbox()
    {
        var name = new DataSyncFieldOutcome("name", DataSyncFieldResolution.Conflict, new DataSyncDisplayValue("Artist"),
            new DataSyncDisplayValue("作者"), new DataSyncDisplayValue("Artists"), new DataSyncDisplayValue("作者"));
        return
        [
            // Merger-derived (§9.1).
            Item(1, DataSyncInboxItemType.FieldConflict, DataSyncInboxItemOrigin.Merger, "name",
                Payload("作者", fields: [name]),
                [DataSyncInboxAction.KeepLocal, DataSyncInboxAction.UseRemote, DataSyncInboxAction.UseCustom,
                    DataSyncInboxAction.Detach]),
            Item(2, DataSyncInboxItemType.ChildRenameConflict, DataSyncInboxItemOrigin.Merger, "choice:c-horror",
                Payload("Genre", fields:
                [
                    new DataSyncFieldOutcome("choice:c-horror", DataSyncFieldResolution.Conflict,
                        new DataSyncDisplayValue("Horror"), new DataSyncDisplayValue("Horror films"),
                        new DataSyncDisplayValue("恐怖"), new DataSyncDisplayValue("Horror films")),
                ], usageCount: 30),
                [DataSyncInboxAction.KeepLocal, DataSyncInboxAction.UseRemote, DataSyncInboxAction.UseCustom,
                    DataSyncInboxAction.Detach]),
            Item(TypeChangeItemId, DataSyncInboxItemType.TypeChange, DataSyncInboxItemOrigin.Merger, "type",
                Payload("Genre", valueCount: 1234, remoteSubtype: "SingleChoice", localSubtype: "MultipleChoice"),
                [DataSyncInboxAction.Convert, DataSyncInboxAction.Detach, DataSyncInboxAction.KeepLocal]),
            Item(4, DataSyncInboxItemType.DeletedThere, DataSyncInboxItemOrigin.Merger, "",
                Payload("Mood", valueCount: 412),
                [DataSyncInboxAction.DeleteHere, DataSyncInboxAction.KeepHereOnly,
                    DataSyncInboxAction.RestoreEverywhere], "14"),
            Item(5, DataSyncInboxItemType.DeletedHereEditedThere, DataSyncInboxItemOrigin.Merger, "",
                Payload("Mood", "SingleChoice"),
                [DataSyncInboxAction.RestoreHere, DataSyncInboxAction.KeepDeleted], null),
            Item(6, DataSyncInboxItemType.LinkSuggestion, DataSyncInboxItemOrigin.Merger, "",
                Payload("Rating", "Rating", candidates:
                [
                    new DataSyncInboxCandidate("15", "Rating", "Rating", DataSyncNaturalMatch.Exact, true),
                    new DataSyncInboxCandidate("16", "rating", "Number", DataSyncNaturalMatch.Clash, false),
                ]),
                [DataSyncInboxAction.Link, DataSyncInboxAction.KeepBoth, DataSyncInboxAction.Skip], null),
            // Row I: one record, two entities here.
            Item(7, DataSyncInboxItemType.IdentityConflict, DataSyncInboxItemOrigin.Merger, "",
                Payload("Artist", "SingleLineText", candidates:
                [
                    new DataSyncInboxCandidate("17", "Artist", "SingleLineText", DataSyncNaturalMatch.Exact, true),
                    new DataSyncInboxCandidate("18", "Author", "SingleLineText", DataSyncNaturalMatch.None, true),
                ]),
                [DataSyncInboxAction.KeepWithEntity, DataSyncInboxAction.Detach], "17"),
            // Row M: two records, one entity here.
            Item(8, DataSyncInboxItemType.IdentityConflict, DataSyncInboxItemOrigin.Merger, "",
                Payload("Artist", "SingleLineText", records:
                [
                    new DataSyncInboxRecordRef("3a1e2d3c4b5a69788796a5b4c3d2e1f0", "Artist", "SingleLineText"),
                    new DataSyncInboxRecordRef("4a1e2d3c4b5a69788796a5b4c3d2e1f0", "Author", "SingleLineText"),
                ]),
                [DataSyncInboxAction.KeepRecordLinked, DataSyncInboxAction.Detach], "17"),

            // State-derived (§9.1).
            Item(9, DataSyncInboxItemType.ChildDeletedInUse, DataSyncInboxItemOrigin.State, "choice:c-horror",
                Payload("Genre", usageCount: 30, children: [new DataSyncDisplayValue("Horror", "#aa0000")],
                    childrenTotal: 1),
                [DataSyncInboxAction.DeleteHere, DataSyncInboxAction.KeepHereOnly,
                    DataSyncInboxAction.RestoreEverywhere]),
            Item(10, DataSyncInboxItemType.MassChildDeletion, DataSyncInboxItemOrigin.State, "",
                Payload("Genre", "Tags",
                    children: Enumerable.Range(1, 50).Select(i => new DataSyncDisplayValue($"Tag {i}", Group: "Genre"))
                        .ToList(),
                    childrenTotal: 180),
                [DataSyncInboxAction.ReviewEach, DataSyncInboxAction.ApplyAll, DataSyncInboxAction.RestoreEverywhere]),
            // Belongs to no link (§8.1).
            Item(11, DataSyncInboxItemType.SuspectedLostUpdate, DataSyncInboxItemOrigin.State, "",
                Payload("Genre", fields:
                [
                    new DataSyncFieldOutcome("choice:c-horror", DataSyncFieldResolution.Unchanged,
                        new DataSyncDisplayValue("Horror films"), new DataSyncDisplayValue("Horror"), null, null),
                ], peerName: null),
                [DataSyncInboxAction.Publish, DataSyncInboxAction.Reapply], linkId: null),
            // Link level: no kind, no entity (§9.1 K).
            Item(12, DataSyncInboxItemType.LargeChange, DataSyncInboxItemOrigin.State, "largeChange",
                Payload("", null, largeChange:
                [
                    new DataSyncLargeChangeEntry("Genre", DataSyncKindIds.CustomProperty, false, 40),
                    new DataSyncLargeChangeEntry("Studio", DataSyncKindIds.CustomProperty, true, 1),
                ], childrenTotal: 182),
                [DataSyncInboxAction.ApplyAll], null, kind: ""),

            // Closed on another device (§9.3).
            Item(13, DataSyncInboxItemType.FieldConflict, DataSyncInboxItemOrigin.Merger, "name",
                    Payload("Artist", fields: [name]),
                    [DataSyncInboxAction.KeepLocal, DataSyncInboxAction.UseRemote, DataSyncInboxAction.UseCustom,
                        DataSyncInboxAction.Detach], "19") with
                {
                    ClosedAt = Now.AddMinutes(-30), Closure = DataSyncInboxClosure.ResolvedElsewhere,
                    Action = DataSyncInboxAction.UseRemote, ClosedByName = "Laptop"
                },
        ];
    }

    private static DataSyncChangeCounts Counts(int set = 0, int add = 0, int rename = 0, int recolor = 0) =>
        new(set + add + rename + recolor, set, add, rename, recolor);

    private static DataSyncPlanItem PlanItem(string key, string kind, DataSyncPlanItemType type, string name,
        string? subtype, DataSyncPlanEntity? local = null, IReadOnlyList<DataSyncPlanCandidate>? candidates = null,
        IReadOnlyList<DataSyncFieldChange>? changes = null, DataSyncPlanItemReason? reason = null,
        DataSyncHeldReason? heldReason = null, IReadOnlyList<DataSyncPlanResolution>? allowed = null,
        DataSyncPlanResolution? defaultResolution = null, bool requiresConfirmation = false,
        bool bulkLinkEligible = false)
    {
        changes ??= [];
        return new DataSyncPlanItem($"{kind}/k/{key}", kind, type, reason, heldReason,
            new DataSyncPlanEntity(null, name, subtype, 0, 3), local, candidates ?? [], changes,
            Counts(set: changes.Count(c => c.Kind == DataSyncFieldChangeKind.Set),
                add: changes.Count(c => c.Kind == DataSyncFieldChangeKind.AddChild)),
            false, local == null ? 0 : 3, 0, allowed ?? [], defaultResolution,
            defaultResolution is DataSyncPlanResolution.Update or DataSyncPlanResolution.Link
                ? local?.LocalKey ?? candidates?.FirstOrDefault()?.LocalKey
                : null,
            requiresConfirmation, bulkLinkEligible, type == DataSyncPlanItemType.NeedsDecision, local != null,
            $"token-{key}", [], [], false);
    }

    private static DataSyncReviewResult CannedReview()
    {
        const string cp = DataSyncKindIds.CustomProperty;
        var exact = new DataSyncPlanCandidate("15", "Rating", "Rating", DataSyncNaturalMatch.Exact, [], Counts(), false,
            [], [], false, 0, 0, true, "token-candidate-15");
        var items = new List<DataSyncPlanItem>
        {
            PlanItem("a0000000000000000000000000000001", cp, DataSyncPlanItemType.Create, "Studio", "SingleLineText",
                allowed: [DataSyncPlanResolution.Create, DataSyncPlanResolution.Skip],
                defaultResolution: DataSyncPlanResolution.Create),
            PlanItem("a0000000000000000000000000000002", cp, DataSyncPlanItemType.Update, "Genre", "Tags",
                local: new DataSyncPlanEntity("12", "Genre", "Tags", 1, 40),
                changes:
                [
                    new DataSyncFieldChange("add:1", DataSyncFieldChangeKind.AddChild, "tags", null,
                        new DataSyncDisplayValue("Isekai", Group: "Genre"), null, null),
                ],
                allowed: [DataSyncPlanResolution.Update, DataSyncPlanResolution.Skip],
                defaultResolution: DataSyncPlanResolution.Update),
            PlanItem("a0000000000000000000000000000003", cp, DataSyncPlanItemType.Unchanged, "Mood", "SingleChoice",
                local: new DataSyncPlanEntity("14", "Mood", "SingleChoice", 2, 5),
                allowed: [DataSyncPlanResolution.Update, DataSyncPlanResolution.Skip],
                defaultResolution: DataSyncPlanResolution.Update),
            PlanItem("a0000000000000000000000000000004", cp, DataSyncPlanItemType.Link, "Rating", "Rating",
                candidates: [exact],
                allowed: [DataSyncPlanResolution.Link, DataSyncPlanResolution.CreateSeparate, DataSyncPlanResolution.Skip],
                defaultResolution: DataSyncPlanResolution.Link, requiresConfirmation: true, bulkLinkEligible: true),
            PlanItem("a0000000000000000000000000000005", cp, DataSyncPlanItemType.NeedsDecision, "Artist",
                "SingleLineText",
                candidates:
                [
                    exact with {LocalKey = "17", Name = "Artist", Subtype = "SingleLineText", ReviewToken = "token-17"},
                    exact with
                    {
                        LocalKey = "18", Name = "artist", Subtype = "SingleLineText", Match = DataSyncNaturalMatch.Similar,
                        ReviewToken = "token-18"
                    },
                ],
                reason: DataSyncPlanItemReason.AmbiguousNameMatch,
                allowed: [DataSyncPlanResolution.Link, DataSyncPlanResolution.CreateSeparate, DataSyncPlanResolution.Skip],
                requiresConfirmation: true),
            PlanItem("a0000000000000000000000000000006", cp, DataSyncPlanItemType.Held, "Huge tags", "Tags",
                heldReason: DataSyncHeldReason.TooLarge),
        };
        var extensionGroups = new List<DataSyncPlanItem>
        {
            PlanItem("b0000000000000000000000000000001", DataSyncKindIds.ExtensionGroup, DataSyncPlanItemType.Create,
                "Video", null, allowed: [DataSyncPlanResolution.Create, DataSyncPlanResolution.Skip],
                defaultResolution: DataSyncPlanResolution.Create),
        };
        var sections = new List<DataSyncPlanKindSection>
        {
            new(DataSyncKindIds.ExtensionGroup, 1, true, extensionGroups, 7),
            new(cp, 1, true, items, 94),
        };
        var summary = new DataSyncPlanSummary(
            sections.SelectMany(s => s.Items.GroupBy(i => i.Type)
                    .Select(g => new DataSyncKindTypeCount(s.Kind, g.Key, g.Count())))
                .ToList(),
            sections.Sum(s => s.Items.Count(i => i.RequiresConfirmation)),
            sections.Sum(s => s.Items.Count(i => i.BulkLinkEligible)),
            sections.Sum(s => s.Items.Count(i => i.Type == DataSyncPlanItemType.Held)));
        var plan = new DataSyncPlan(PlanId, new string('e', 64), sections, summary, []);
        return new DataSyncReviewResult(ReviewId, 3, false, DataSyncLinkMode.TwoWay, DataSyncReviewState.Staged,
            new DataSyncReviewSource("node-laptop", "Laptop", "2.5.0-beta.10", Now.AddMinutes(-2),
                [new DataSyncKindCount(DataSyncKindIds.ExtensionGroup, 1), new DataSyncKindCount(cp, 6)]),
            plan, null, null, null, null);
    }

    private static IReadOnlyList<DataSyncEntityStatusView> CannedEntities() =>
    [
        new("12", "0f1e2d3c4b5a69788796a5b4c3d2e1f0", DataSyncEntitySyncState.Synced, false, 2, 1, "node-nas", "NAS",
            "NAS", Now.AddMinutes(-5), 4, false, null),
        new("13", "1f1e2d3c4b5a69788796a5b4c3d2e1f0", DataSyncEntitySyncState.Synced, true, 0, 0, "node-self",
            "This PC", "This PC", Now.AddMinutes(-5), 0, true, DataSyncHeldReason.PendingDecision),
        new("20", "5f1e2d3c4b5a69788796a5b4c3d2e1f0", DataSyncEntitySyncState.LocalOnly, false, 0, 0, null, null, null,
            null, 0, false, null),
        new("21", "6f1e2d3c4b5a69788796a5b4c3d2e1f0", DataSyncEntitySyncState.Detached, false, 0, 0, "node-nas", "NAS",
            "This PC", Now.AddDays(-2), 0, false, null),
        new("ext-1", "2f1e2d3c4b5a69788796a5b4c3d2e1f0", DataSyncEntitySyncState.Synced, false, 0, 0, "node-nas", "NAS",
            "NAS", Now.AddMinutes(-5), 0, false, null),
    ];

    private static IReadOnlyList<DataSyncHistoryEntry> CannedHistory() =>
        Enum.GetValues<DataSyncHistoryKind>()
            .Select((kind, i) => new DataSyncHistoryEntry(i + 1, Now.AddHours(-i), kind,
                kind is DataSyncHistoryKind.Resolution or DataSyncHistoryKind.EntitySetting ? null : 1,
                kind is DataSyncHistoryKind.Resolution or DataSyncHistoryKind.EntitySetting ? null : "node-nas",
                kind is DataSyncHistoryKind.Resolution or DataSyncHistoryKind.EntitySetting ? null : "NAS",
                new DataSyncHistoryCounts(1, 1, 0, 3, 0, 0, 0, 1, 0, 0, 0, kind == DataSyncHistoryKind.Resolution ? 2 : 0),
                kind == DataSyncHistoryKind.Undo ? DataSyncUndoState.Expired : DataSyncUndoState.Available, null))
            .ToList();

    private static DataSyncRestoreView CannedRestore() =>
        new(true, DataSyncPauseReason.LocalRestoreDetected, Now.AddMinutes(-20), 1, null, null, "NAS", BackupPath);
}
