using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// How one definition syncs (§3.6, §6.6 "After an entity setting changes"): its state (keep on this device only, stop
/// syncing, sync again), the shared "definition only" field, and the options kept on this device only. It runs
/// synchronously in the request, under the gate the caller holds, after the actor check and in one short transaction
/// with a Refresh, so the change becomes a revision at once; no task is started (N15). Each change is a history entry
/// that undo can take back.
/// </summary>
public sealed class DataSyncEntitySettings
{
    private readonly IServiceProvider _services;

    /// <param name="services">A scope.</param>
    public DataSyncEntitySettings(IServiceProvider services)
    {
        _services = services;
    }

    private IDataSyncStore Store => _services.GetRequiredService<IDataSyncStore>();
    private DataSyncLimits Limits => _services.GetService<DataSyncLimits>() ?? DataSyncLimits.Default;
    private DateTime Now => _services.GetService<IDataSyncClock>()?.UtcNow ?? DateTime.UtcNow;

    /// <summary>The setting as it will be: each member null when it stays as it is.</summary>
    private sealed record Target(DataSyncEntitySyncState? State, bool? ChildrenLocal, DataSyncOverlay? Overlay);

    public async Task<DataSyncTaskStart> SetAsync(string kind, string localKey, DataSyncEntitySyncInput input,
        DataSyncGateHold gate, CancellationToken ct)
    {
        var adapter = (_services.GetService<IEnumerable<IDataSyncKind>>() ?? [])
            .FirstOrDefault(k => k.Codec.Descriptor.Kind == kind);
        if (!DataSyncKindIds.All.Contains(kind) || adapter is null) return Refuse(DataSyncProblemCode.UnknownKind, kind);
        var add = input.AddLocalOnlyChildren ?? [];
        var remove = input.RemoveLocalOnlyChildren ?? [];
        if (input.State is null && input.ChildrenLocal is null && add.Count == 0 && remove.Count == 0)
            return Refuse(DataSyncProblemCode.NothingSelected, null);
        if (input.State is { } state && !Enum.IsDefined(state))
            return Refuse(DataSyncProblemCode.DecisionsInvalid, "state");
        if (input.ChildrenLocal is not null && !adapter.Codec.Descriptor.SupportsChildrenLocal)
            return Refuse(DataSyncProblemCode.DecisionsInvalid, "childrenLocal");
        if (add.Concat(remove).Any(c => string.IsNullOrWhiteSpace(c) || c.Length > Limits.MaxUuidLength) ||
            add.Intersect(remove, StringComparer.Ordinal).Any())
            return Refuse(DataSyncProblemCode.DecisionsInvalid, "child");

        var entity = (await Store.GetEntitiesAsync(kind, false, ct))
            .FirstOrDefault(e => e.LocalKey == localKey && e.DeletedAtUtc is null);
        if (entity is null) return Refuse(DataSyncProblemCode.UnknownItem, "entity");

        var overlay = DataSyncViews.ReadOverlay(entity.OverlayJson);
        var before = new DataSyncEntitySettingPreImage(kind, localKey, entity.State, entity.ChildrenLocal, overlay);
        var localOnly = overlay.LocalOnlyChildren.Except(remove, StringComparer.Ordinal)
            .Concat(add).Distinct(StringComparer.Ordinal).ToList();
        // A held child kept on this device only is no longer held: its item's state is gone (§9.2 KeepHereOnly).
        var held = overlay.HeldChildren.Where(h => !add.Contains(h.ChildId, StringComparer.Ordinal)).ToList();
        var overlayChanged = held.Count != overlay.HeldChildren.Count ||
                             !localOnly.OrderBy(c => c, StringComparer.Ordinal).SequenceEqual(
                                 overlay.LocalOnlyChildren.OrderBy(c => c, StringComparer.Ordinal));
        var target = new Target(
            input.State is { } s && s != entity.State ? s : null,
            input.ChildrenLocal is { } c && c != entity.ChildrenLocal ? c : null,
            overlayChanged ? new DataSyncOverlay(localOnly, held) : null);
        if (target is { State: null, ChildrenLocal: null, Overlay: null }) return new DataSyncTaskStart(null, null);

        // §5.6: the actor check after entering the gate and before any transaction.
        var guard = _services.GetService<IDataSyncActorGuard>();
        if (guard is not null) await guard.CheckAsync(gate.Lease, ct);

        for (var attempt = 0;; attempt++)
        {
            try
            {
                var logId = await ApplyAsync(adapter, entity, before, target, gate.Lease, ct);
                await _services.GetRequiredService<IDataSyncRuntimeObserver>()
                    .WriteAppliedAsync(DataSyncHistoryKind.EntitySetting, logId, null, ct);
                return new DataSyncTaskStart(null, null);
            }
            catch (DataSyncActorChangedException) when (attempt == 0 && guard is not null)
            {
                // Refresh found the actor rotated under it: the transaction rolled back; check, then once more (§5.6).
                await guard.CheckAsync(gate.Lease, ct);
            }
        }
    }

    /// <summary>
    /// The change in one short transaction: the side-row writes, the items it settles, a Refresh that makes the
    /// shared change a revision, and the history entry. Every write sets the target value, so running it again after a
    /// rollback writes the same thing.
    /// </summary>
    private async Task<int> ApplyAsync(IDataSyncKind adapter, DataSyncEntityDbModel entity,
        DataSyncEntitySettingPreImage before, Target target, DataSyncGateLease lease, CancellationToken ct)
    {
        var db = _services.GetService<BakabaseDbContext>();
        await using var tx = db is null ? null : await db.Database.BeginTransactionAsync(ct);
        var store = Store;
        var (kind, localKey) = (entity.Kind, entity.LocalKey);
        var key = new SyncKey(entity.SyncKey);
        var now = Now;

        if (target.State is { } state)
        {
            await store.SetEntityStateAsync(kind, localKey, state, ct);
            // Keeping it here only or stopping to sync it closes all of its items (§9.3 "Other closures").
            if (state is DataSyncEntitySyncState.LocalOnly or DataSyncEntitySyncState.Detached)
            {
                var open = (await store.GetOpenItemsAsync(null, ct))
                    .Where(i => i.Kind == kind && i.Key == key).Select(i => i.Id).ToList();
                if (open.Count > 0)
                    await store.CloseItemsAsync(open, DataSyncInboxClosure.Superseded, null, null, null, ct);
            }
        }

        if (target.ChildrenLocal is { } childrenLocal)
            await store.SetChildrenLocalAsync(kind, localKey, childrenLocal, ct);

        if (target.Overlay is { } overlay)
        {
            await store.SetOverlayAsync(kind, localKey, overlay, ct);
            if (overlay.HeldChildren.Count != before.Overlay.HeldChildren.Count)
                await store.CloseStaleStateItemsAsync([(kind, key)], null, now, ct);
        }

        // The shared field and what is published changed: Refresh makes it a revision now (§3.6, §6.6).
        var refresher = _services.GetService<IDataSyncRefresher>();
        if (refresher is not null) await refresher.RefreshAsync(lease, [kind], false, ct);

        var item = new DataSyncHistoryItem($"{kind}/k/{entity.SyncKey}", kind, await NameOfAsync(adapter, localKey, ct),
            DataSyncItemOutcome.Applied, DataSyncItemAction.Updated, localKey, DataSyncPlanItemType.Update);
        var preImage = DataSyncHistoryJson.WriteEntitySettingPreImage([before]);
        var logId = await store.AddHistoryAsync(new DataSyncApplyLogDbModel
        {
            Kind = DataSyncHistoryKind.EntitySetting,
            AppliedAtUtc = now,
            SummaryJson = DataSyncHistoryJson.WriteSummary(DataSyncHistoryJson.CountItems([item])),
            ResultJson = DataSyncHistoryJson.WriteResult([item]),
            PreImageJson = preImage,
            PreImageBytes = System.Text.Encoding.UTF8.GetByteCount(preImage),
        }, ct);

        if (tx is not null) await tx.CommitAsync(ct);
        return logId;
    }

    private static async Task<string> NameOfAsync(IDataSyncKind adapter, string localKey, CancellationToken ct)
    {
        try
        {
            var local = (await adapter.ReadAsync([localKey], ct)).FirstOrDefault();
            return local is null ? localKey : adapter.Codec.NameOf(adapter.Codec.ReadLocal(local.Content));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return localKey;
        }
    }

    private static DataSyncTaskStart Refuse(DataSyncProblemCode code, string? detail) =>
        new(null, new DataSyncProblem(code, detail));
}
