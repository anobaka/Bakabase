using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// A first sync on the device that started the link, and a copy once (§8.3): a read-only preview of the ordinary merge
/// over the snapshot staged for the link, and its Start, which runs that merge as a task
/// (<see cref="DataSyncTaskLauncher.EnqueueFirstSyncAsync"/>).
/// </summary>
public sealed class DataSyncFirstSync(IServiceProvider services)
{
    private DataSyncRuntimeState State => services.GetRequiredService<DataSyncRuntimeState>();

    /// <summary>
    /// The preview: what a Start would do now, merged against the last committed local state in a read transaction,
    /// without Refresh and without any write (F78); the Start's own Refresh and merge take in anything newer. No snapshot
    /// while it is still fetched. Never gated.
    /// </summary>
    public async Task<DataSyncFirstSyncPreview> GetAsync(int linkId, CancellationToken ct)
    {
        var link = await services.GetRequiredService<IDataSyncStore>().GetLinkAsync(linkId, ct);
        if (link is null)
        {
            return new DataSyncFirstSyncPreview(linkId, false, DataSyncLinkMode.Off, DataSyncLinkState.Stopped, null, [],
                null, new DataSyncProblem(DataSyncProblemCode.LinkNotFound, null));
        }

        var taskId = DataSyncTaskIds.Review(linkId);
        var running = services.GetRequiredService<BTaskManager>().GetTaskViewModel(taskId)?.Status
            .IsActiveOrPending() == true;
        var preview = new DataSyncFirstSyncPreview(link.Id, link.Mode == DataSyncLinkMode.Off, link.Mode, link.State,
            null, [], running ? taskId : null, null);
        if (link.State != DataSyncLinkState.AwaitingReview || State.PeekPreview(linkId) is not { } pull) return preview;
        var source = new DataSyncReviewSource(pull.PeerNodeId, pull.PeerName, pull.Manifest.AppVersion,
            DataSyncViews.Utc(pull.FetchedAtUtc),
            pull.Kinds.Select(k => new DataSyncKindCount(k.Kind, k.Entities.Count)).ToList());
        return preview with { Source = source, Entries = await PreviewAsync(link, pull, ct) };
    }

    /// <summary>
    /// Start: refused unless the link awaits its first sync with a snapshot staged, or when a choice names no record of
    /// that snapshot, or links or keeps both outside a copy once (its name matches are answered in the preview; any
    /// other link asks them under "Needs you" after the Start). Not gated: the task takes the gate.
    /// </summary>
    public async Task<DataSyncTaskStart> StartAsync(int linkId, DataSyncFirstSyncStartInput input, CancellationToken ct)
    {
        var link = await services.GetRequiredService<IDataSyncStore>().GetLinkAsync(linkId, ct);
        if (link is null) return Refused(DataSyncProblemCode.LinkNotFound, null);
        if (link.State != DataSyncLinkState.AwaitingReview || State.PeekPreview(linkId) is not { } pull)
            return Refused(DataSyncProblemCode.NothingToReview, null);
        var records = pull.Kinds.SelectMany(k => k.Entities.Select(e => (k.Kind, e.Record.Keys[0]))).ToHashSet();
        var copyOnce = link.Mode == DataSyncLinkMode.Off;
        var choices = input.Choices ?? [];
        if (choices.FirstOrDefault(c => !records.Contains((c.Kind, c.Key)) ||
                                        (c.Action != DataSyncFirstSyncAction.Skip && !copyOnce) ||
                                        (c.Action == DataSyncFirstSyncAction.Link) != (c.LocalKey is not null)) is
            { } invalid)
        {
            return Refused(DataSyncProblemCode.DecisionsInvalid, invalid.Key);
        }

        var attempt = await services.GetRequiredService<DataSyncTaskLauncher>().EnqueueFirstSyncAsync(linkId, choices);
        return attempt is null
            ? Refused(DataSyncProblemCode.ApplyInProgress, DataSyncTaskIds.Review(linkId))
            : new DataSyncTaskStart(attempt.TaskId, null);
    }

    /// <summary>
    /// The merge a Start would run, one entry per record it evaluates, told apart by what the result writes on the
    /// record's base row: an operation there, a name match or another question, a hold, an exclusion, or nothing to do.
    /// Without a local state row nothing here takes part in sync yet, and every readable record is a create.
    /// </summary>
    private async Task<IReadOnlyList<DataSyncPreviewEntry>> PreviewAsync(DataSyncLinkDbModel link,
        DataSyncStagedPull pull, CancellationToken ct)
    {
        await using var s = await DataSyncApplySession.OpenAsync(services.GetRequiredService<IServiceScopeFactory>(), ct);
        var incoming = pull.Kinds.SelectMany(k => k.Entities.Select(e => (k.Kind, Entity: e)))
            .ToDictionary(x => (x.Kind, x.Entity.Record.Keys[0]), x => x.Entity);
        var entries = await s.Reader.InReadTransactionAsync(async () =>
        {
            if (await s.Store.GetLocalStateAsync(ct) is null)
            {
                return incoming.Where(x => !x.Value.Record.Deleted).Select(x => Entry(s, x.Key.Kind, x.Value,
                    x.Value.Held is null ? DataSyncPreviewOutcome.Create : DataSyncPreviewOutcome.Held)).ToList();
            }

            await s.LoadStateAsync(ct);
            var copyOnce = link.Mode == DataSyncLinkMode.Off ? new DataSyncCopyOnce(
                new Dictionary<(string Kind, string Key), string>(), new HashSet<(string Kind, string Key)>()) : null;
            var input = await DataSyncMergeInputs.BuildAsync(s, DataSyncMergeInputs.LinkContext(s, link, copyOnce),
                pull, [], ct);
            var result = DataSyncMerger.Merge(input);
            var operations = result.Batches.SelectMany(b => b.Operations).GroupBy(o => o.ItemId)
                .ToDictionary(g => g.Key, g => g.First());
            var drafts = result.Inbox.GroupBy(d => (d.Kind, d.Key.Value)).ToDictionary(g => g.Key, g => g.First());
            string? LocalName(string kind, string localKey) =>
                input.Local.GetValueOrDefault(kind)?.Entities.FirstOrDefault(l => l.LocalKey == localKey) is { } l
                    ? s.Kinds[kind].Codec.NameOf(l.Content)
                    : null;

            var list = new List<DataSyncPreviewEntry>();
            foreach (var u in result.BaseUpdates)
            {
                var record = u.Pending?.Record ?? u.Record;
                if (record is null || !incoming.TryGetValue((u.Kind, record.Keys[0]), out var entity)) continue;
                var draft = drafts.GetValueOrDefault((u.Kind, u.Key.Value));
                var operation = operations.GetValueOrDefault(DataSyncMergeItemIds.Of(u.Kind, u.Key));
                var name = draft?.Payload.EntityName ??
                           (operation is DeleteEntityOperation deleted ? LocalName(u.Kind, deleted.LocalKey) : null);
                var outcome = operation switch
                {
                    CreateEntityOperation => DataSyncPreviewOutcome.Create,
                    UpdateEntityOperation => DataSyncPreviewOutcome.Update,
                    DeleteEntityOperation => DataSyncPreviewOutcome.Delete,
                    _ when u.State == DataSyncBaseState.Excluded => DataSyncPreviewOutcome.NotSynced,
                    _ when u.Pending?.Reason is DataSyncPendingReason.Held or DataSyncPendingReason.PublishHeld =>
                        DataSyncPreviewOutcome.Held,
                    _ when draft?.Type == DataSyncInboxItemType.LinkSuggestion => DataSyncPreviewOutcome.NameMatch,
                    _ when u.Pending is not null => DataSyncPreviewOutcome.Question,
                    _ => DataSyncPreviewOutcome.Unchanged,
                };
                list.Add(Entry(s, u.Kind, entity, outcome, name,
                    outcome == DataSyncPreviewOutcome.NameMatch ? draft!.Payload.Candidates : null));
            }

            return list;
        }, ct);
        return entries.OrderBy(e => DataSyncKindIds.All.ToList().IndexOf(e.Kind)).ThenBy(e => e.Outcome)
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <param name="name">What the merge calls it (a definition here it deletes), else the record's own name.</param>
    private static DataSyncPreviewEntry Entry(DataSyncApplySession s, string kind, DataSyncIncomingEntity entity,
        DataSyncPreviewOutcome outcome, string? name = null, IReadOnlyList<DataSyncInboxCandidate>? candidates = null) =>
        new(kind, entity.Record.Keys[0], name ?? entity.DisplayName,
            entity.Content is { } content && s.Kinds.TryGetValue(kind, out var adapter)
                ? adapter.Codec.SubtypeOf(content)
                : null,
            outcome, candidates);

    private static DataSyncTaskStart Refused(DataSyncProblemCode code, string? detail) =>
        new(null, new DataSyncProblem(code, detail));
}
