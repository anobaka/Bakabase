using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Planning;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Persistence;

/// <summary>
/// The lost-update guard (§6.5). A whole-row writer that read a definition before a sync apply committed and wrote it
/// back afterwards silently undoes what sync applied; Refresh would publish that stale overwrite as a newer local
/// revision and revert the change on every device. When the current local content undoes at least one change of the
/// entity's most recent apply of the <see cref="Window"/> before the change may have been made, Refresh holds the
/// entity instead (no revision, <c>PublishHeld</c>, a <c>SuspectedLostUpdate</c> item) until a person decides.
/// </summary>
/// <remarks>
/// <para>
/// A change counts as undone, by path and by local child id (engineering must-fix 14), when: a scalar is back at its
/// before value (and before ≠ after); an added child's id is gone; a renamed child's id carries its old label; a
/// moved node's id is back under its old parent; a removed child's id is back. Recolours and order are appearance and
/// never held. Only apply logs of kind AutoSync, Resolution and FirstLink count; data sync's own undo is exempt (its
/// logs are Undo, and an undone log no longer counts).
/// </para>
/// <para>
/// The window is measured to the change, not to the Refresh that meets it: nothing runs Refresh on a timer, and one
/// that comes long after a stale write (the device's readers offline, its only link a Follow) must still judge it.
/// Refresh cannot tell when a change was made, only that it was made after the kind's last committed Refresh began
/// (<see cref="WindowStart"/>, <c>RefreshedAtJson</c>), so every guarded apply of the window before that moment
/// counts. The scheduler refreshes once each window has closed (<see cref="DataSyncRefreshCoordinator"/>), so a
/// person's deliberate revert after it is an ordinary revision again.
/// </para>
/// </remarks>
public static class DataSyncLostUpdateGuard
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Where the window starts for a change Refresh meets now in <paramref name="kind"/>: <see cref="Window"/> before
    /// the kind's last committed Refresh began (the change came after it), or before <paramref name="nowUtc"/> when no
    /// Refresh of the kind was ever recorded.
    /// </summary>
    public static DateTime WindowStart(IReadOnlyDictionary<string, long> refreshedAt, string kind, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(refreshedAt);
        var lastLook = refreshedAt.TryGetValue(kind, out var ms) ? FromUnixMs(ms) : nowUtc;
        return (lastLook < nowUtc ? lastLook : nowUtc) - Window;
    }

    /// <summary>
    /// <c>RefreshedAtJson</c> after a Refresh of <paramref name="kinds"/> that began at
    /// <paramref name="startedAtUtc"/>, or null when it need not change. A recorded time only moves while a guarded apply lies inside the window before
    /// it: otherwise an older time widens the window over nothing but older applies, and heads, which refresh every
    /// few seconds, would write the row each time for nothing.
    /// </summary>
    public static async Task<string?> AfterRefreshAsync(BakabaseDbContext db, string? refreshedAtJson,
        IReadOnlyCollection<string> kinds, DateTime startedAtUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        var refreshedAt = new Dictionary<string, long>(
            DataSyncStoredJson.ReadCounters(refreshedAtJson, "RefreshedAtJson"), StringComparer.Ordinal);
        var started = ToUnixMs(startedAtUtc);
        var recorded = kinds.Where(refreshedAt.ContainsKey).Select(k => refreshedAt[k]).ToList();
        // Only the applies inside the oldest recorded time's window matter; a kind never recorded is written anyway.
        var latest = recorded.Count == 0
            ? null
            : await LatestGuardedApplyAsync(db, FromUnixMs(recorded.Min()) - Window, ct);
        var changed = false;
        foreach (var kind in kinds)
        {
            if (refreshedAt.TryGetValue(kind, out var last) &&
                (last >= started || latest is not { } at || at < FromUnixMs(last) - Window))
            {
                continue;
            }

            refreshedAt[kind] = started;
            changed = true;
        }

        return changed ? DataSyncStoredJson.WriteCounters(refreshedAt) : null;
    }

    /// <summary>
    /// The most recent guarded apply that was not undone (§6.5), applied at or after <paramref name="sinceUtc"/> when
    /// given; null when there is none.
    /// </summary>
    public static async Task<DateTime?> LatestGuardedApplyAsync(BakabaseDbContext db, DateTime? sinceUtc,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var logs = db.DataSyncApplyLogs.AsNoTracking()
            .Where(l => l.UndoneAtUtc == null && (l.Kind == DataSyncHistoryKind.AutoSync ||
                                                  l.Kind == DataSyncHistoryKind.Resolution ||
                                                  l.Kind == DataSyncHistoryKind.FirstLink));
        if (sinceUtc is { } since) logs = logs.Where(l => l.AppliedAtUtc >= since);
        var latest = await logs.MaxAsync(l => (DateTime?) l.AppliedAtUtc, ct);
        return latest is { } at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : null;
    }

    public static long ToUnixMs(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    public static DateTime FromUnixMs(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;

    /// <summary>
    /// The item's <c>Detail</c> once Reapply cannot run: the apply's change list is gone (retention, or the apply was
    /// undone), so nothing says what to write back. The item then offers Publish only.
    /// </summary>
    public const string ReapplyUnavailable = DataSyncInboxRules.ReapplyUnavailableDetail;

    /// <summary>The apply kinds whose changes a whole-row writer can undo (§6.5).</summary>
    public static readonly IReadOnlyList<DataSyncHistoryKind> GuardedKinds =
        [DataSyncHistoryKind.AutoSync, DataSyncHistoryKind.Resolution, DataSyncHistoryKind.FirstLink];

    private const string ChildrenLocalPath = "childrenLocal";
    private const string OrderKeyPath = "orderKey";

    /// <summary>
    /// The changes of <paramref name="changes"/> that the current local content undoes, as the item shows them:
    /// base = before the apply, remote = what the apply wrote, local = now.
    /// </summary>
    public static IReadOnlyList<DataSyncFieldOutcome> FindUndone(IDataSyncKindCodec codec, JsonObject currentContent,
        bool currentChildrenLocal, DataSyncEntityChanges changes)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(currentContent);
        ArgumentNullException.ThrowIfNull(changes);
        var undone = new List<DataSyncFieldOutcome>();
        foreach (var scalar in changes.Scalars)
        {
            if (scalar.Path == OrderKeyPath || JsonNode.DeepEquals(scalar.Before, scalar.After)) continue;
            var now = scalar.Path == ChildrenLocalPath
                ? JsonValue.Create(currentChildrenLocal)
                : ReadPath(currentContent, scalar.Path);
            var before = scalar.Path == ChildrenLocalPath ? scalar.Before ?? JsonValue.Create(false) : scalar.Before;
            if (!JsonNode.DeepEquals(now, before)) continue;
            undone.Add(new DataSyncFieldOutcome(scalar.Path, DataSyncFieldResolution.TookRemote, Display(scalar.Before),
                Display(now), Display(scalar.After), Display(scalar.After)));
        }

        if (changes.Children.Count == 0) return undone;

        var current = new Dictionary<string, DataSyncChildInfo>(StringComparer.Ordinal);
        foreach (var child in codec.ChildrenOf(codec.ReadLocal(currentContent))) current.TryAdd(child.Id, child);
        foreach (var change in changes.Children)
        {
            var now = current.GetValueOrDefault(change.ChildId);
            var isUndone = (change.Before, change.After) switch
            {
                (null, not null) => now is null,                                    // added, now gone
                (not null, null) => now is not null,                                // removed, now back
                ({ } before, { } after) =>
                    now is not null &&
                    ((!SameLabel(before, after) && SameLabel(now, before)) ||       // renamed back
                     (!string.Equals(before.ParentId, after.ParentId, StringComparison.Ordinal) &&
                      string.Equals(now.ParentId, before.ParentId, StringComparison.Ordinal))), // moved back
                _ => false,
            };
            if (!isUndone) continue;
            undone.Add(new DataSyncFieldOutcome(change.Path, DataSyncFieldResolution.TookRemote, change.Before?.Display,
                now?.Display, change.After?.Display, change.After?.Display));
        }

        return undone;
    }

    /// <summary>
    /// The item's <c>Detail</c> while "Put the synced change back" would remove children resources here use
    /// (<see cref="ReapplyInUseAsync"/>).
    /// </summary>
    public const string ReapplyInUse = DataSyncInboxRules.ReapplyInUseDetail;

    /// <summary>
    /// The children "Put the synced change back" would remove from <paramref name="current"/> (the removals of the
    /// entity's most recent guarded apply the local content undid) that resources here use: Reapply never removes
    /// one (§8.5.4 step 3 holds them in a merge, undo refuses <c>AddedOptionsInUse</c>).
    /// </summary>
    public static async Task<IReadOnlyList<DataSyncDisplayValue>> ReapplyInUseAsync(IDataSyncKind adapter,
        string localKey, object current, bool currentChildrenLocal, DataSyncEntityChanges applied, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var codec = adapter.Codec;
        var undone = DataSyncChangeLists.Undone(codec, current, currentChildrenLocal, applied);
        var edit = DataSyncChangeLists.Apply(codec, current, currentChildrenLocal, undone.Scalars, undone.Children,
            backward: false);
        return await InUseAsync(adapter, localKey, current, edit.RemovedChildIds, ct);
    }

    /// <summary>Of <paramref name="childIds"/>, those resources here use, as the card shows them.</summary>
    public static async Task<IReadOnlyList<DataSyncDisplayValue>> InUseAsync(IDataSyncKind adapter, string localKey,
        object current, IReadOnlyCollection<string> childIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        if (childIds.Count == 0) return [];
        var usage = await adapter.GetUsageAsync(
            new Dictionary<string, IReadOnlyCollection<string>> { [localKey] = childIds }, ct);
        var counts = usage.GetValueOrDefault(localKey)?.ResourceCountByChildId;
        if (counts is null) return [];
        var displays = new Dictionary<string, DataSyncDisplayValue>(StringComparer.Ordinal);
        foreach (var child in adapter.Codec.ChildrenOf(current)) displays.TryAdd(child.Id, child.Display);
        return childIds.Distinct(StringComparer.Ordinal).Where(id => counts.GetValueOrDefault(id) > 0)
            .Select(id => displays.GetValueOrDefault(id) ?? new DataSyncDisplayValue(id)).ToList();
    }

    /// <summary>
    /// The item's payload with the children Reapply would remove that resources here use (<see cref="ReapplyInUse"/>),
    /// or without them when there are none.
    /// </summary>
    public static DataSyncInboxPayload WithReapplyInUse(DataSyncInboxPayload payload,
        IReadOnlyList<DataSyncDisplayValue> inUse)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(inUse);
        if (inUse.Count > 0)
        {
            return payload with
            {
                Children = inUse.Take(DataSyncInboxDrafts.MaxListed).ToList(), ChildrenTotal = inUse.Count,
                Detail = ReapplyInUse,
            };
        }

        return payload.Detail == ReapplyInUse
            ? payload with { Children = null, ChildrenTotal = 0, Detail = null }
            : payload;
    }

    /// <summary>The state-derived item (§9.1 J): of no link, subject the whole entity, the undone changes as its fields.</summary>
    /// <param name="inUse">The children Reapply would remove that resources here use (<see cref="ReapplyInUseAsync"/>).</param>
    public static DataSyncInboxDraft Draft(string kind, SyncKey key, string localKey, string entityName, string? subtype,
        string? peerName, IReadOnlyList<DataSyncFieldOutcome> undone, DataSyncVersionVector localVv,
        IReadOnlyList<DataSyncDisplayValue>? inUse = null)
    {
        var payload = WithReapplyInUse(new DataSyncInboxPayload(entityName, subtype, peerName, null, null, undone, null,
            null, null, 0, null, null, null, null, null), inUse ?? []);
        return new DataSyncInboxDraft(kind, key, localKey, DataSyncInboxItemType.SuspectedLostUpdate,
            DataSyncInboxItemOrigin.State, "", payload, null, null, localVv, DataSyncMergeFlags.None,
            DataSyncInboxTokens.Of(DataSyncInboxItemType.SuspectedLostUpdate, "", undone));
    }

    /// <summary>A member of the local content by dotted path; null when absent.</summary>
    internal static JsonNode? ReadPath(JsonObject content, string path)
    {
        JsonNode? node = content;
        foreach (var member in path.Split('.'))
        {
            if (node is not JsonObject obj || !obj.TryGetPropertyValue(member, out node)) return null;
        }

        return node;
    }

    private static bool SameLabel(DataSyncChildInfo a, DataSyncChildInfo b) =>
        string.Equals(a.Display.Text, b.Display.Text, StringComparison.Ordinal) &&
        string.Equals(a.Display.Group, b.Display.Group, StringComparison.Ordinal);

    private static DataSyncDisplayValue? Display(JsonNode? value) => value switch
    {
        null => null,
        JsonValue v when v.GetValueKind() == JsonValueKind.String => new DataSyncDisplayValue(v.GetValue<string>()),
        JsonValue v when v.GetValueKind() is JsonValueKind.True or JsonValueKind.False =>
            new DataSyncDisplayValue(null, Flag: v.GetValue<bool>()),
        JsonValue v when v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<int>(out var number) =>
            new DataSyncDisplayValue(null, Number: number),
        _ => new DataSyncDisplayValue(value.ToJsonString()),
    };
}

/// <summary>An entity's most recent guarded apply (§6.5), with its change list.</summary>
public sealed record DataSyncAppliedChanges(int LogId, DataSyncHistoryKind Kind, string? PeerName,
    DateTime AppliedAtUtc, DataSyncEntityChanges Changes);

/// <summary>
/// The guarded apply logs as the lost-update guard reads them. Loaded only when Refresh meets a change, since most
/// Refreshes find none.
/// </summary>
public sealed class DataSyncAppliedChangesIndex
{
    private const int ScanPageSize = 50;

    private readonly Dictionary<(string Kind, string LocalKey), DataSyncAppliedChanges> _latest;

    private DataSyncAppliedChangesIndex(Dictionary<(string Kind, string LocalKey), DataSyncAppliedChanges> latest) =>
        _latest = latest;

    /// <summary>
    /// Each entity's most recent guarded apply applied at or after <paramref name="sinceUtc"/> (the earliest
    /// <see cref="DataSyncLostUpdateGuard.WindowStart"/> of the kinds a Refresh covers).
    /// </summary>
    public static async Task<DataSyncAppliedChangesIndex> LoadRecentAsync(BakabaseDbContext db, DateTime sinceUtc,
        CancellationToken ct)
    {
        var since = sinceUtc;
        var latest = new Dictionary<(string Kind, string LocalKey), DataSyncAppliedChanges>();
        foreach (var log in await Guarded(db).Where(l => l.AppliedAtUtc >= since).ToListAsync(ct))
            AddEntities(latest, log, null);
        return new DataSyncAppliedChangesIndex(latest);
    }

    public DataSyncAppliedChanges? Get(string kind, string localKey) => _latest.GetValueOrDefault((kind, localKey));

    /// <summary>
    /// The entity's most recent guarded apply of any age, for refreshing the item of an entity already held after the
    /// window passed; retention bounds the scan.
    /// </summary>
    public static async Task<DataSyncAppliedChanges?> FindLatestAsync(BakabaseDbContext db, string kind,
        string localKey, CancellationToken ct)
    {
        for (var skip = 0;; skip += ScanPageSize)
        {
            var page = await Guarded(db).Skip(skip).Take(ScanPageSize).ToListAsync(ct);
            var found = new Dictionary<(string Kind, string LocalKey), DataSyncAppliedChanges>();
            foreach (var log in page)
            {
                AddEntities(found, log, (kind, localKey));
                if (found.TryGetValue((kind, localKey), out var hit)) return hit;
            }

            if (page.Count < ScanPageSize) return null;
        }
    }

    /// <summary>Guarded logs that were not undone, newest first.</summary>
    private static IQueryable<DataSyncApplyLogDbModel> Guarded(BakabaseDbContext db) =>
        db.DataSyncApplyLogs.AsNoTracking()
            .Where(l => l.UndoneAtUtc == null && (l.Kind == DataSyncHistoryKind.AutoSync ||
                                                  l.Kind == DataSyncHistoryKind.Resolution ||
                                                  l.Kind == DataSyncHistoryKind.FirstLink))
            .OrderByDescending(l => l.AppliedAtUtc).ThenByDescending(l => l.Id);

    private static void AddEntities(Dictionary<(string Kind, string LocalKey), DataSyncAppliedChanges> into,
        DataSyncApplyLogDbModel log, (string Kind, string LocalKey)? only)
    {
        foreach (var entity in DataSyncApplyResultDocument.ReadEntities(log.ResultJson))
        {
            var key = (entity.Kind, entity.LocalKey);
            if (only is { } wanted && key != wanted) continue;
            // Logs arrive newest first: the first one that names an entity is its most recent.
            into.TryAdd(key, new DataSyncAppliedChanges(log.Id, log.Kind, log.PeerName,
                DateTime.SpecifyKind(log.AppliedAtUtc, DateTimeKind.Utc), entity));
        }
    }
}
