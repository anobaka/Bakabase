using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Apply;

/// <summary>One entity of an undo, as planned: what undo does to it, or why it refuses (§8.11).</summary>
/// <param name="Setting">An entity setting's entry: what the entity's setting was before (§6.6).</param>
internal sealed record DataSyncUndoStep(DataSyncEntityPreImage PreImage, DataSyncUndoAction Action,
    DataSyncUndoBlock? Blocked, int? ValueCount, bool SettingsMayReferenceIt, bool RecreatedGetsNewId,
    bool Destructive, DataSyncEntitySettingPreImage? Setting = null)
{
    public DataSyncUndoPreviewItem ToView() => new(PreImage.Kind, PreImage.LocalKey, PreImage.Name, Action, Blocked,
        ValueCount, SettingsMayReferenceIt, RecreatedGetsNewId);
}

/// <summary>
/// What undoing one history entry would do (§8.11), computed the same way for the preview (read-only, never gated)
/// and inside the undo task: per entity, newest first, the action and its refusal —
/// <list type="bullet">
/// <item><c>ChangedSinceImport</c>: a newer retained apply that was not undone touched the entity (v3.1 N15), a created
/// entity changed since, one of the changed paths changed since, a deleted entity came back, a converted type
/// changed again;</item>
/// <item><c>InUse</c>: a created custom property has values;</item>
/// <item><c>AddedOptionsInUse</c>: resources use a child the apply added;</item>
/// <item><c>Missing</c>: the entity is gone (or a type change's entry kept no raw row to restore).</item>
/// </list>
/// The undo task can still refuse a step while writing it: the faithfulness check (§8.11) takes back a step whose
/// entity, re-read, is not what it restored.
/// It is the facade's <see cref="IDataSyncUndoPreviewer"/>.
/// </summary>
public sealed class DataSyncUndoPlanner(IServiceScopeFactory scopes) : IDataSyncUndoPreviewer
{
    /// <summary>The preview of an entry the facade read (§10.1): no gate, no writes.</summary>
    public Task<DataSyncUndoPreview> PreviewAsync(DataSyncApplyLogDbModel entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return PreviewAsync(entry.Id, ct);
    }

    /// <summary>The preview of <c>GET /data-sync/history/{id}/undo</c> (§10.1): no gate, no writes.</summary>
    public async Task<DataSyncUndoPreview> PreviewAsync(int logId, CancellationToken ct)
    {
        await using var s = await DataSyncApplySession.OpenAsync(scopes, ct);
        var (_, steps, problem) = await PlanAsync(s, logId, ct);
        if (problem is not null) return new DataSyncUndoPreview(false, [], problem);
        return new DataSyncUndoPreview(steps.Any(x => x.Blocked is null) || HasKeyMoves(s, logId),
            steps.Select(x => x.ToView()).ToList(), null);
    }

    private static bool HasKeyMoves(DataSyncApplySession s, int logId)
    {
        var log = s.Db.DataSyncApplyLogs.AsNoTracking().SingleOrDefault(l => l.Id == logId);
        return log is not null && DataSyncPreImageDocument.Read(log.PreImageJson).Identity is { KeyMoves.Count: > 0 };
    }

    internal static async Task<(DataSyncApplyLogDbModel? Log, List<DataSyncUndoStep> Steps, DataSyncProblem? Problem)>
        PlanAsync(DataSyncApplySession s, int logId, CancellationToken ct)
    {
        await s.Store.FlushAsync(ct);
        var log = await s.Db.DataSyncApplyLogs.SingleOrDefaultAsync(l => l.Id == logId, ct);
        if (log is null || log.Kind == DataSyncHistoryKind.Undo || log.UndoneAtUtc is not null)
            return (log, [], new DataSyncProblem(DataSyncProblemCode.UndoNotAvailable, null));
        var document = DataSyncPreImageDocument.Read(log.PreImageJson);

        // Newest-first per entity (v3.1 N15): a newer retained apply that is not undone owns the entity's state, and a
        // newer entity setting that is not undone owns how it syncs.
        var newer = new HashSet<int>();
        var newerSettings = new HashSet<(string Kind, string LocalKey)>();
        foreach (var json in await s.Db.DataSyncApplyLogs.AsNoTracking()
                     .Where(l => l.Id > logId && l.UndoneAtUtc == null && l.Kind != DataSyncHistoryKind.Undo)
                     .Select(l => l.PreImageJson).ToListAsync(ct))
        {
            var later = DataSyncPreImageDocument.Read(json);
            foreach (var entity in later.Entities) newer.Add(entity.EntityId);
            foreach (var setting in later.EntitySettings ?? []) newerSettings.Add((setting.Kind, setting.LocalKey));
        }

        var steps = new List<DataSyncUndoStep>();
        foreach (var entity in document.Entities.Reverse())
        {
            steps.Add(await PlanEntityAsync(s, entity, newer.Contains(entity.EntityId), ct));
        }

        foreach (var setting in (document.EntitySettings ?? []).Reverse())
        {
            steps.Add(await PlanSettingAsync(s, setting, newerSettings.Contains((setting.Kind, setting.LocalKey)), ct));
        }

        return (log, steps, null);
    }

    /// <summary>
    /// An entity setting (§6.6) is undone by setting its state, shared <c>childrenLocal</c> and overlay back; refused
    /// when the entity is gone, or a newer entity setting of it has not been undone.
    /// </summary>
    private static async Task<DataSyncUndoStep> PlanSettingAsync(DataSyncApplySession s,
        DataSyncEntitySettingPreImage setting, bool newerSetting, CancellationToken ct)
    {
        var row = await s.Db.DataSyncEntities.AsNoTracking().SingleOrDefaultAsync(e =>
            e.Kind == setting.Kind && e.LocalKey == setting.LocalKey && e.DeletedAtUtc == null, ct);
        var adapter = s.Kinds.GetValueOrDefault(setting.Kind);
        var name = setting.LocalKey;
        if (row is not null && adapter is not null)
        {
            var entity = (await adapter.ReadAsync([row.LocalKey], ct)).SingleOrDefault();
            if (entity is not null) name = adapter.Codec.NameOf(adapter.Codec.ReadLocal(entity.Content));
        }

        var preImage = new DataSyncEntityPreImage(setting.Kind, row?.Id ?? 0, setting.LocalKey, name,
            DataSyncPreImageActions.EntitySetting, null, [], null);
        DataSyncUndoBlock? blocked = row is null || adapter is null ? DataSyncUndoBlock.Missing
            : newerSetting ? DataSyncUndoBlock.ChangedSinceImport
            : null;
        return new DataSyncUndoStep(preImage, DataSyncUndoAction.Revert, blocked, null, false, false, false, setting);
    }

    private static async Task<DataSyncUndoStep> PlanEntityAsync(DataSyncApplySession s, DataSyncEntityPreImage p,
        bool newerApply, CancellationToken ct)
    {
        var action = p.Action switch
        {
            DataSyncPreImageActions.Created => DataSyncUndoAction.Remove,
            DataSyncPreImageActions.Deleted => DataSyncUndoAction.Recreate,
            DataSyncPreImageActions.Bound => DataSyncUndoAction.Exclude,
            _ => DataSyncUndoAction.Revert,
        };
        var row = await s.Db.DataSyncEntities.AsNoTracking().SingleOrDefaultAsync(e => e.Id == p.EntityId, ct);
        DataSyncUndoStep Step(DataSyncUndoBlock? blocked, int? values = null, bool destructive = false) =>
            new(p, action, blocked, values, action == DataSyncUndoAction.Remove, action == DataSyncUndoAction.Recreate,
                destructive);

        if (row is null || row.Kind != p.Kind || !s.Kinds.TryGetValue(p.Kind, out var adapter))
            return Step(DataSyncUndoBlock.Missing);
        if (newerApply) return Step(DataSyncUndoBlock.ChangedSinceImport);
        var live = row.DeletedAtUtc is null;

        switch (p.Action)
        {
            case DataSyncPreImageActions.Created:
            {
                if (!live) return Step(DataSyncUndoBlock.Missing);
                var usage = await adapter.GetUsageAsync(
                    new Dictionary<string, IReadOnlyCollection<string>> { [row.LocalKey] = [] }, ct);
                var values = usage.GetValueOrDefault(row.LocalKey)?.ValueCount ?? 0;
                if (values > 0) return Step(DataSyncUndoBlock.InUse, values);
                if (p.AfterLocalHash is not null && row.LocalHash != p.AfterLocalHash)
                    return Step(DataSyncUndoBlock.ChangedSinceImport, values);
                return Step(null, values);
            }
            case DataSyncPreImageActions.Deleted:
                return live || p.Content is null ? Step(DataSyncUndoBlock.ChangedSinceImport) : Step(null);
            case DataSyncPreImageActions.Bound:
                return live ? Step(null) : Step(DataSyncUndoBlock.Missing);
            case DataSyncPreImageActions.TypeChanged:
            {
                // Converting back restores the captured raw row (IDataSyncKind.RestoreAsync): without one there is
                // nothing to restore.
                if (!live || p.Row is null || p.Content is null) return Step(DataSyncUndoBlock.Missing);
                var current = adapter.Codec.ReadLocal((await adapter.ReadAsync([row.LocalKey], ct)).Single().Content);
                if (adapter.Codec.SubtypeOf(current) != p.ToSubtype || p.FromSubtype is null)
                    return Step(DataSyncUndoBlock.ChangedSinceImport);
                var preview = await adapter.PreviewSubtypeChangeAsync(row.LocalKey, p.FromSubtype, ct);
                return Step(null, preview.ValueCount, preview.LossyCount > 0);
            }
            default:
            {
                if (!live) return Step(DataSyncUndoBlock.Missing);
                if (p.Changes is null) return Step(null);
                var entity = (await adapter.ReadAsync([row.LocalKey], ct)).SingleOrDefault();
                if (entity is null) return Step(DataSyncUndoBlock.Missing);
                var current = adapter.Codec.ReadLocal(entity.Content);
                var edit = DataSyncChangeLists.Apply(adapter.Codec, current, row.ChildrenLocal, p.Changes.Scalars,
                    p.Changes.Children, backward: true);
                if (edit.Conflicts.Count > 0) return Step(DataSyncUndoBlock.ChangedSinceImport);
                if (edit.RemovedChildIds.Count > 0)
                {
                    var usage = await adapter.GetUsageAsync(new Dictionary<string, IReadOnlyCollection<string>>
                    {
                        [row.LocalKey] = edit.RemovedChildIds.ToList(),
                    }, ct);
                    if (usage.GetValueOrDefault(row.LocalKey)?.ResourceCountByChildId.Values.Any(v => v > 0) == true)
                        return Step(DataSyncUndoBlock.AddedOptionsInUse);
                }

                return Step(null);
            }
        }
    }
}
