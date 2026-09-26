using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// How one definition syncs (§3.6, §6.6 "After an entity setting changes"): its state (keep on this device only, stop
/// syncing, sync again), the shared "definition only" field, and the options kept on this device only. It runs
/// synchronously in the request, under the gate the caller holds, through <see cref="IDataSyncLocalChangeRunner"/>:
/// after the actor check and in one short transaction with a Refresh, committed only while the actor is verified, so
/// the change becomes a revision at once; no task is started (N15). Each change is a history entry that undo can take
/// back.
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

        var name = await NameOfAsync(adapter, localKey, ct);
        int? logId = null;
        try
        {
            // The actor check after entering the gate and before any transaction (§5.6), then the change and a Refresh
            // that makes the shared change a revision, in one transaction committed only while the actor is still
            // verified; an attempt the actor changed under is rolled back and run again in a new scope.
            await _services.GetRequiredService<IDataSyncLocalChangeRunner>().RunAsync(gate.Lease, [kind],
                async (scope, c) => logId = await WriteAsync(scope.GetRequiredService<IDataSyncStore>(), entity, before,
                    target, name, c), ct);
        }
        catch (Exception e) when (e is DataSyncActorUnverifiedException or DataSyncActorChangedException)
        {
            // Evidence kept arriving, or the actor kept changing, under every attempt: nothing stands; ask again.
            return Refuse(DataSyncProblemCode.Busy, null);
        }

        await _services.GetRequiredService<IDataSyncRuntimeObserver>()
            .WriteAppliedAsync(DataSyncHistoryKind.EntitySetting, logId, null, ct);
        return new DataSyncTaskStart(null, null);
    }

    /// <summary>
    /// The change, in the transaction the local change runner opened on <paramref name="store"/>'s scope: the side-row
    /// writes, the items it settles and the history entry; the runner's Refresh then makes the shared change a
    /// revision. Every write sets the target value, so an attempt run again after a rollback writes the same thing.
    /// </summary>
    private async Task<int> WriteAsync(IDataSyncStore store, DataSyncEntityDbModel entity,
        DataSyncEntitySettingPreImage before, Target target, string name, CancellationToken ct)
    {
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

        var item = new DataSyncHistoryItem($"{kind}/k/{entity.SyncKey}", kind, name, DataSyncItemOutcome.Applied,
            DataSyncItemAction.Updated, localKey, DataSyncPlanItemType.Update);
        var preImage = DataSyncHistoryJson.WriteEntitySettingPreImage([before]);
        return await store.AddHistoryAsync(new DataSyncApplyLogDbModel
        {
            Kind = DataSyncHistoryKind.EntitySetting,
            AppliedAtUtc = now,
            SummaryJson = DataSyncHistoryJson.WriteSummary(DataSyncHistoryJson.CountItems([item])),
            ResultJson = DataSyncHistoryJson.WriteResult([item]),
            PreImageJson = preImage,
            PreImageBytes = System.Text.Encoding.UTF8.GetByteCount(preImage),
        }, ct);
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
