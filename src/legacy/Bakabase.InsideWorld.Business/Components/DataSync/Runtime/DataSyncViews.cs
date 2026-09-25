using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// The read models of the <c>/data-sync</c> page, the map and the status indicator (§2.10, §11), built from the stored
/// rows, the federation grants and what the runtime knows since it started. Reads only: nothing here writes or waits
/// for the DataSyncGate, so every view answers while an apply holds it (§10.1 "never gated", F78). Every time is UTC.
/// </summary>
public sealed class DataSyncViews
{
    /// <summary>The peer is away: shown grey as offline, never as an error (§8.2).</summary>
    private static readonly HashSet<string> OfflineCodes =
        [nameof(DataSyncPeerErrorCode.Unreachable), nameof(DataSyncPeerErrorCode.Busy)];

    /// <summary>Something went wrong that retrying alone may not fix: shown as "Sync failed" (§11.6).</summary>
    private static readonly HashSet<string> FailureCodes =
    [
        DataSyncLinkService.ApplyFailed, DataSyncLinkService.FetchFailed,
        nameof(DataSyncPeerErrorCode.InvalidResponse), nameof(DataSyncPeerErrorCode.TooLarge),
    ];

    private readonly IServiceProvider _services;
    private readonly IDataSyncStore _store;
    private readonly IDataSyncGrantService _grants;

    /// <param name="services">A scope: the store and the grant service are resolved from it.</param>
    public DataSyncViews(IServiceProvider services)
    {
        _services = services;
        _store = services.GetRequiredService<IDataSyncStore>();
        _grants = services.GetRequiredService<IDataSyncGrantService>();
    }

    private DataSyncRuntimeState? State => _services.GetService<DataSyncRuntimeState>();
    private IDataSyncReviewStore? Reviews => _services.GetService<IDataSyncReviewStore>();
    private DateTime Now => _services.GetService<IDataSyncClock>()?.UtcNow ?? DateTime.UtcNow;

    /// <summary>Everything several views share, read once.</summary>
    public sealed record Snapshot(IReadOnlyList<DataSyncLinkDbModel> Links, IReadOnlyList<DataSyncOpenInboxItem> OpenItems,
        IReadOnlyList<DataSyncGrantView> Grants, IReadOnlyList<DataSyncReaderDbModel> Readers,
        DataSyncLocalStateDbModel? Local)
    {
        public int OpenItemsOf(int linkId) => OpenItems.Count(i => i.LinkId == linkId);

        public DataSyncReaderDbModel? ReaderOf(string nodeId) =>
            Readers.FirstOrDefault(r => string.Equals(r.NodeId, nodeId, StringComparison.Ordinal));

        public DataSyncGrantView? GrantOf(string nodeId) =>
            Grants.FirstOrDefault(g => string.Equals(g.NodeId, nodeId, StringComparison.Ordinal));
    }

    public async Task<Snapshot> ReadAsync(CancellationToken ct) =>
        new(await _store.GetLinksAsync(ct), await _store.GetOpenItemsAsync(null, ct), await _grants.GetGrantsAsync(ct),
            await _store.GetReadersAsync(ct), await _store.GetLocalStateAsync(ct));

    // ---- links -------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<DataSyncLinkView>> GetLinksAsync(CancellationToken ct)
    {
        var snapshot = await ReadAsync(ct);
        var views = new List<DataSyncLinkView>();
        foreach (var link in snapshot.Links.OrderBy(l => l.Id)) views.Add(await ToViewAsync(link, snapshot, ct));
        return views;
    }

    public async Task<DataSyncLinkView?> GetLinkAsync(int linkId, CancellationToken ct)
    {
        var snapshot = await ReadAsync(ct);
        var link = snapshot.Links.FirstOrDefault(l => l.Id == linkId);
        return link is null ? null : await ToViewAsync(link, snapshot, ct);
    }

    public async Task<DataSyncLinkView> ToViewAsync(DataSyncLinkDbModel link, Snapshot snapshot, CancellationToken ct)
    {
        int pending = 0, excluded = 0, held = 0, missing = 0;
        foreach (var kind in DataSyncKindIds.All)
        {
            foreach (var b in await _store.GetBasesAsync(link.Id, kind, ct))
            {
                if (b.Pending is not null) pending++;
                if (b.State == DataSyncBaseState.Excluded) excluded++;
                if (b.State == DataSyncBaseState.MissingAtPeer) missing++;
                if (b.State == DataSyncBaseState.Held || b.Pending?.Reason == DataSyncPendingReason.Held) held++;
            }
        }

        var counterpart = link.GetCounterpart();
        var reader = snapshot.ReaderOf(link.PeerNodeId);
        return new DataSyncLinkView(link.Id, link.PeerNodeId, link.PeerName, link.PeerAddress, link.Mode, link.LastMode,
            link.State, link.PausedReason, link.PausedDetail, link.Initiator, link.GetKinds(), counterpart?.Kinds,
            Utc(link.LastSyncedAtUtc), Utc(link.NextAttemptAtUtc), link.LastErrorCode, link.LastErrorDetail,
            snapshot.OpenItemsOf(link.Id), pending, CurrentReviewId(link), link.PeerAppVersion, link.PeerContractVersion,
            snapshot.GrantOf(link.PeerNodeId) is not null, link.ReadBackDeclined,
            counterpart?.Mode ?? reader?.Mode, Utc(reader?.LastReadAtUtc), link.GetPeerAttention(), excluded, held,
            missing, IsOnline(link), Utc(link.GetStartAnywayAt()));
    }

    /// <summary>The review a person can open for this link now: the staged one, unless it expired (§8.3).</summary>
    public string? CurrentReviewId(DataSyncLinkDbModel link) => Reviews?.GetForLink(link.Id)?.ReviewId;

    private bool IsOnline(DataSyncLinkDbModel link) =>
        State?.GetLastHeadAt(link.Id) is not null && link.ConsecutiveFailures == 0 &&
        !link.State.IsPeerErrorState() && (link.LastErrorCode is null || !OfflineCodes.Contains(link.LastErrorCode));

    // ---- status ------------------------------------------------------------------------------------------------

    /// <summary>
    /// The indicator's one line (§11.6), most urgent first: a pending restore or the global pause, decisions here, an
    /// update this device needs, a failure, a paused link, an offline peer, a running sync, else in step. Off while
    /// nothing syncs.
    /// </summary>
    public DataSyncStatusView GetStatus(Snapshot snapshot, bool syncing)
    {
        var links = snapshot.Links.Where(l => l.State != DataSyncLinkState.Stopped).ToList();
        var open = snapshot.OpenItems.Count;
        var inStep = links.Count(l => l.State == DataSyncLinkState.Active && l.ConsecutiveFailures == 0 &&
                                      l.LastErrorCode is null && snapshot.OpenItemsOf(l.Id) == 0);
        var needThere = links.Count(l => l.GetPeerAttention() is { OpenDecisions: > 0 });
        var lastSynced = snapshot.Links.Select(l => l.LastSyncedAtUtc).Where(t => t is not null).Max();
        var failing = links.Where(l => l.LastErrorCode is not null)
            .OrderByDescending(l => l.LastAttemptAtUtc ?? DateTime.MinValue).FirstOrDefault();

        DataSyncStatusLevel level;
        if (links.Count == 0 && open == 0 && snapshot.Local?.RestoreReason is null) level = DataSyncStatusLevel.Off;
        else if (snapshot.Local is { RestoreReason: not null } or { AllPaused: true }) level = DataSyncStatusLevel.Paused;
        else if (open > 0) level = DataSyncStatusLevel.NeedsYou;
        else if (links.Any(l => l.State == DataSyncLinkState.ThisTooOld)) level = DataSyncStatusLevel.UpdateNeeded;
        else if (links.Any(IsFailed)) level = DataSyncStatusLevel.Failed;
        else if (links.Any(l => l.State == DataSyncLinkState.Paused)) level = DataSyncStatusLevel.Paused;
        else if (links.Any(l => l.LastErrorCode is not null && OfflineCodes.Contains(l.LastErrorCode)))
            level = DataSyncStatusLevel.Offline;
        else if (syncing) level = DataSyncStatusLevel.Syncing;
        else level = DataSyncStatusLevel.InStep;

        return new DataSyncStatusView(level, open, links.Count, inStep, needThere, Utc(lastSynced),
            failing?.LastErrorCode);
    }

    /// <summary>A failure that is not the peer being away: an apply or a fetch that went wrong, or an unusable answer.</summary>
    private static bool IsFailed(DataSyncLinkDbModel link) =>
        link.LastErrorCode is { } code && FailureCodes.Contains(code);

    /// <summary>Whether a data sync task is running or waiting to write.</summary>
    public bool IsSyncing()
    {
        var launcher = _services.GetService<DataSyncTaskLauncher>();
        var btm = _services.GetService<BTaskManager>();
        var fetching = btm?.GetTaskViewModel(DataSyncTaskIds.Fetch)?.Status.IsActive() == true;
        return fetching || launcher?.IsWriteTaskActiveOrPending() == true;
    }

    public async Task<DataSyncStatusView> GetStatusAsync(CancellationToken ct) => GetStatus(await ReadAsync(ct), IsSyncing());

    // ---- the map -----------------------------------------------------------------------------------------------

    /// <summary>
    /// What the device map draws for data sync (§11.1): every device this one syncs with or that reads it, requests
    /// others filed (claims, M5), and this device's own links that wait for access or whose request ended.
    /// </summary>
    public async Task<DataSyncMapView> GetMapAsync(CancellationToken ct)
    {
        var snapshot = await ReadAsync(ct);
        var requests = await _grants.GetRequestsAsync(ct);
        var now = Now;

        var outgoing = new List<DataSyncMapOutgoing>();
        var peers = new List<DataSyncMapPeer>();
        var nodes = snapshot.Links.Select(l => l.PeerNodeId).Concat(snapshot.Grants.Select(g => g.NodeId))
            .Distinct(StringComparer.Ordinal).ToList();
        foreach (var node in nodes)
        {
            var link = snapshot.Links.FirstOrDefault(l => string.Equals(l.PeerNodeId, node, StringComparison.Ordinal));
            var grant = snapshot.GrantOf(node);
            var reader = snapshot.ReaderOf(node);
            if (link is not null && OutcomeOf(link) is { } outcome)
            {
                var request = link.PendingRequestId is { } requestId
                    ? requests.FirstOrDefault(r => r.Direction == DataSyncRequestDirection.Outgoing &&
                                                   string.Equals(r.RequestId, requestId, StringComparison.Ordinal))
                    : null;
                outgoing.Add(new DataSyncMapOutgoing(link.Id, node, link.PeerName, link.PeerAddress, link.State,
                    outcome, request is null ? null : Utc(request.ExpiresAt)));
                if (grant is null) continue;
            }

            var receiving = link is { State: DataSyncLinkState.Active } && link.Mode != DataSyncLinkMode.Off;
            var receivingPending = link is not null && link.Mode != DataSyncLinkMode.Off &&
                                   link.State is DataSyncLinkState.AwaitingReview
                                       or DataSyncLinkState.WaitingForPeerReview or DataSyncLinkState.AwaitingAccess;
            var counterpart = link?.GetCounterpart();
            peers.Add(new DataSyncMapPeer(node, link?.PeerName ?? grant?.Name ?? reader?.Name ?? node, link?.Id,
                link?.Mode ?? DataSyncLinkMode.Off, link?.LastMode ?? DataSyncLinkMode.TwoWay, link?.State,
                link?.State == DataSyncLinkState.Paused ? link.PausedReason : null, receiving, receivingPending,
                grant is not null, reader?.Mode ?? counterpart?.Mode, counterpart?.Kinds, Utc(reader?.LastReadAtUtc),
                Utc(link?.LastSyncedAtUtc), link is null ? 0 : snapshot.OpenItemsOf(link.Id), link?.GetPeerAttention(),
                link?.ReadBackDeclined ?? false, link?.LastErrorCode, link?.GetKinds() ?? []));
        }

        var incoming = requests
            .Where(r => r.Direction == DataSyncRequestDirection.Incoming && IsPending(r, now))
            .Select(r => new DataSyncMapRequest(r.RequestId, r.NodeId, r.NodeName, r.RemoteAddress, r.Intent,
                Utc(r.ExpiresAt), r.ClaimsKnownDevice, r.KnownAddress))
            .ToList();

        return new DataSyncMapView(await _grants.IsSharingEnabledAsync(ct), await _grants.GetRemoteAccessModeAsync(ct),
            peers, incoming, outgoing);
    }

    /// <summary>
    /// A link of this device's own that the map shows as an outgoing request (M5): waiting for access it asked for,
    /// or stopped because the request was rejected or expired, until the person dismisses it.
    /// </summary>
    private static string? OutcomeOf(DataSyncLinkDbModel link)
    {
        if (link.Initiator != DataSyncLinkInitiator.ThisDevice) return null;
        if (link.State == DataSyncLinkState.AwaitingAccess) return "awaitingApproval";
        if (link.State != DataSyncLinkState.Stopped) return null;
        return link.LastErrorCode switch
        {
            DataSyncLinkService.AccessRejected => "rejected",
            DataSyncLinkService.AccessExpired => "expired",
            _ => null,
        };
    }

    /// <summary>A request still waiting for a decision.</summary>
    public static bool IsPending(DataSyncAccessRequestView request, DateTime nowUtc) =>
        request.Status?.ToLowerInvariant() is "awaitingapproval" or "pending" && Utc(request.ExpiresAt) > nowUtc;

    // ---- readers -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Who may read this device's definitions (§7.1), with what each declared when it last read (§7.5.6). Up to date:
    /// it was served this device's latest sequence number.
    /// </summary>
    public async Task<IReadOnlyList<DataSyncReaderView>> GetReadersAsync(CancellationToken ct)
    {
        var snapshot = await ReadAsync(ct);
        var lastSeq = snapshot.Local?.LastSeq ?? 0;
        return snapshot.Grants.OrderBy(g => g.Name, StringComparer.Ordinal).Select(g =>
        {
            var reader = snapshot.ReaderOf(g.NodeId);
            return new DataSyncReaderView(g.NodeId, reader?.Name ?? g.Name, Utc(g.GrantedAt),
                Utc(reader?.LastReadAtUtc), reader?.Mode, reader?.State,
                reader is not null && reader.LastSeqServed >= lastSeq);
        }).ToList();
    }

    // ---- entities ----------------------------------------------------------------------------------------------

    /// <summary>
    /// How each definition of a kind syncs (§3.6, §11.3): its state, its overlay, where it came from and who changed
    /// it last, its open decisions, and — for a definition this device receives from a peer — whether it differs from
    /// what that peer last sent (§9.1 I), compared through this build's comparison form (§3.4).
    /// </summary>
    public async Task<IReadOnlyList<DataSyncEntityStatusView>> GetEntitiesAsync(string kind, CancellationToken ct)
    {
        if (!DataSyncKindIds.All.Contains(kind)) return [];
        var snapshot = await ReadAsync(ct);
        var entities = await _store.GetEntitiesAsync(kind, false, ct);
        var codec = _services.GetService<IEnumerable<IDataSyncKind>>()?
            .FirstOrDefault(k => k.Codec.Descriptor.Kind == kind)?.Codec;
        var limits = _services.GetService<DataSyncLimits>() ?? DataSyncLimits.Default;
        var self = snapshot.Local?.NodeId;

        // Per entity key: the base of every link that has one, with that link.
        var bases = new Dictionary<string, List<(DataSyncLinkDbModel Link, DataSyncPeerBase Base)>>(StringComparer.Ordinal);
        foreach (var link in snapshot.Links)
        {
            foreach (var b in await _store.GetBasesAsync(link.Id, kind, ct))
            {
                if (!bases.TryGetValue(b.Key.Value, out var list)) bases[b.Key.Value] = list = [];
                list.Add((link, b));
            }
        }

        var views = new List<DataSyncEntityStatusView>();
        foreach (var entity in entities.Where(e => e.DeletedAtUtc is null))
        {
            var overlay = ReadOverlay(entity.OverlayJson);
            var linked = bases.GetValueOrDefault(entity.SyncKey) ?? [];
            var agreed = linked.Where(x => x.Base.State == DataSyncBaseState.Normal).ToList();
            var differs = codec is not null && agreed.Any(x => x.Link.Mode == DataSyncLinkMode.Follow &&
                                                               x.Link.GetEffectiveMode() == DataSyncLinkMode.Follow &&
                                                               Differs(codec, entity, x.Base, limits));
            var lastSynced = agreed.Select(x => x.Link.LastSyncedAtUtc).Where(t => t is not null).Max();
            views.Add(new DataSyncEntityStatusView(entity.LocalKey, entity.SyncKey, entity.State, entity.ChildrenLocal,
                overlay.LocalOnlyChildren.Count, overlay.HeldChildren.Select(h => h.ChildId).Distinct().Count(),
                entity.OriginNodeId, OriginName(entity.OriginNodeId, self, snapshot),
                entity.LastEditorName, Utc(lastSynced),
                snapshot.OpenItems.Count(i => i.Kind == kind && i.Key.Value == entity.SyncKey), differs,
                HeldAtSourceOf(entity)));
        }

        return views;
    }

    /// <summary>What this device's readers get instead of the entity, when it withholds it (§3.3, §6.5).</summary>
    private static DataSyncHeldReason? HeldAtSourceOf(DataSyncEntityDbModel entity) =>
        entity.Unreadable ? DataSyncHeldReason.LocalUnreadable
        : entity.PublishHeld ? DataSyncHeldReason.PendingDecision
        : null;

    private static string? OriginName(string origin, string? self, Snapshot snapshot)
    {
        if (string.Equals(origin, self, StringComparison.Ordinal)) return null;
        return snapshot.Links.FirstOrDefault(l => string.Equals(l.PeerNodeId, origin, StringComparison.Ordinal))?.PeerName
               ?? snapshot.GrantOf(origin)?.Name ?? snapshot.ReaderOf(origin)?.Name;
    }

    /// <summary>
    /// Whether the entity's comparison form differs from the one of the peer record last agreed on:
    /// <c>ContentHash(codec.ComparisonForm(validated content, orderKey, childrenLocal))</c>, computed by this build for
    /// the base exactly as Refresh computes <c>SharedHash</c> for the entity (§3.4). A base this build cannot read
    /// never counts as a difference.
    /// </summary>
    private static bool Differs(IDataSyncKindCodec codec, DataSyncEntityDbModel entity, DataSyncPeerBase peerBase,
        DataSyncLimits limits)
    {
        try
        {
            var record = peerBase.Record;
            if (record?.Content is not { } content || record.Deleted) return false;
            var read = codec.Read((System.Text.Json.Nodes.JsonObject) content.DeepClone(), limits);
            if (read.Content is null || read.Held is not null) return false;
            var childrenLocal = content["childrenLocal"]?.GetValueKind() == JsonValueKind.True;
            var form = codec.ComparisonForm(read.Content, record.OrderKey, childrenLocal);
            return !string.Equals(ContentHash.Of(form), entity.SharedHash, StringComparison.Ordinal);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return false;
        }
    }

    // ---- helpers -----------------------------------------------------------------------------------------------

    public static DataSyncOverlay ReadOverlay(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return DataSyncOverlay.None;
        try
        {
            return JsonSerializer.Deserialize<DataSyncOverlay>(json, DataSyncJson.Options) ?? DataSyncOverlay.None;
        }
        catch (JsonException)
        {
            return DataSyncOverlay.None;
        }
    }

    /// <summary>
    /// Every <c>/data-sync</c> time is UTC (§2.10): the <c>*Utc</c> columns come back unspecified and are marked as
    /// such; a local time is converted.
    /// </summary>
    public static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    public static DateTime? Utc(DateTime? value) => value is { } v ? Utc(v) : null;
}
