using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Handoffs;

public sealed partial class PostAcquisitionHandoffService
{
    private const string SavedResultIdPrefix = "saved-v1:";
    private const int MaxCatchUpTasks = 1000;

    public async Task<PostAcquisitionFilteredCatchUpPreview> PrepareFilteredSavedResultsAsync(
        PostParserTaskFilterScope input, CancellationToken ct)
    {
        await parserGate.Semaphore.WaitAsync(ct);
        try
        {
            var selected = await postTasks.SelectFilteredTasks(input, MaxCatchUpTasks, ct);
            if (selected.LimitExceeded)
                return new(selected.MatchedCount, MaxCatchUpTasks, true, null);
            var preview = await PrepareSavedResultsUnderGateAsync(selected.Tasks.Select(t => t.TaskId).ToList(), ct);
            return new(selected.MatchedCount, MaxCatchUpTasks, false, preview);
        }
        finally {parserGate.Semaphore.Release();}
    }

    public async Task<PostAcquisitionCatchUpPreview> PrepareSavedResultsAsync(
        PostAcquisitionCatchUpPrepareInput input, CancellationToken ct)
    {
        ValidateTaskIds(input?.TaskIds);
        // This path never refreshes the producer or performs remote preflight. Its only output
        // is a fixed selection for explicit confirmation; GET/list operations never enqueue work.
        await parserGate.Semaphore.WaitAsync(ct);
        try
        {
            return await PrepareSavedResultsUnderGateAsync(input!.TaskIds, ct);
        }
        finally {parserGate.Semaphore.Release();}
    }

    private async Task<PostAcquisitionCatchUpPreview> PrepareSavedResultsUnderGateAsync(
        IReadOnlyList<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new(0, 0, 0, 0, []);
        var facts = await CatchUpFactsAsync(ids, ct);
        var items = ids.Select(id => CatchUpItem(id, facts)).ToList();
        return new(items.Count, items.Count(i => i.Status == "eligible"),
            items.Count(i => i.Status == "existing"), items.Count(i => i.Status == "rejected"), items);
    }

    public async Task<PostAcquisitionCatchUpResult> ConfirmSavedResultsAsync(
        PostAcquisitionCatchUpConfirmInput input, CancellationToken ct)
    {
        if (input?.Tasks == null || input.Tasks.Any(s => s == null))
            throw new ArgumentException("HANDOFF_SELECTION_INVALID");
        ValidateTaskIds(input.Tasks.Select(s => s.TaskId).ToList());
        if (input.Tasks.Any(s => s.Revision < 0 || s.WorkflowRunId is <= 0 ||
                                s.ResultsHash == null || s.ResultsHash.Length != 64 ||
                                s.ResultsHash.Any(c => !char.IsAsciiHexDigit(c))))
            throw new ArgumentException("HANDOFF_SELECTION_INVALID");

        await ConsumerGate.WaitAsync(ct);
        try
        {
            await parserGate.Semaphore.WaitAsync(ct);
            try
            {
                var facts = await CatchUpFactsAsync(input.Tasks.Select(s => s.TaskId).ToList(), ct);
                var items = new List<PostAcquisitionCatchUpItem>();
                var directory = RetryDirectory;
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                foreach (var selection in input.Tasks)
                {
                    var item = CatchUpItem(selection.TaskId, facts);
                    var post = facts.Posts.GetValueOrDefault(selection.TaskId);
                    // Replays are still bound to the submitted source, never to whatever revision
                    // now happens to be associated with this task ID.
                    if (post == null || post.Revision != selection.Revision ||
                        post.WorkflowRunId != selection.WorkflowRunId ||
                        !string.Equals(ResultsHash(post), selection.ResultsHash, StringComparison.OrdinalIgnoreCase))
                    {
                        items.Add(item with {Status = "rejected", Code = "HANDOFF_SOURCE_CHANGED", Selection = null,
                            HandoffId = null, AcquisitionTaskIds = []});
                        continue;
                    }
                    if (item.Status != "eligible") {items.Add(item); continue;}
                    var now = DateTime.UtcNow;
                    var row = new AcquisitionHandoffDbModel
                    {
                        Id = SavedResultId(post), SourceModule = "postParser", SourceKind = "task",
                        SourceId = post.Id.ToString(CultureInfo.InvariantCulture),
                        SourceRevision = post.Revision.ToString(CultureInfo.InvariantCulture),
                        ProducerRunId = post.WorkflowRunId ?? 0, OutputDirectory = directory,
                        CreatedAt = now, UpdatedAt = now
                    };
                    db.AcquisitionHandoffs.Add(row);
                    items.Add(item with {Status = "accepted", HandoffId = row.Id});
                }
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return new(items.Count, items.Count(i => i.Status == "accepted"),
                    items.Count(i => i.Status == "existing"), items.Count(i => i.Status == "rejected"), items);
            }
            finally {parserGate.Semaphore.Release();}
        }
        finally {ConsumerGate.Release();}
    }

    private static void ValidateTaskIds(IReadOnlyList<int>? ids)
    {
        if (ids == null || ids.Count is < 1 or > MaxCatchUpTasks || ids.Any(id => id <= 0) || ids.Distinct().Count() != ids.Count)
            throw new ArgumentException("HANDOFF_SELECTION_INVALID");
    }

    private sealed record CatchUpFacts(Dictionary<int, PostParserTaskDbModel> Posts,
        Dictionary<int, WorkflowRunStatus> Runs,
        Dictionary<(string Id, string Revision), AcquisitionHandoffDbModel> Handoffs,
        HashSet<(string Id, string Revision)> DuplicateHandoffs,
        Dictionary<(string Id, string Revision), List<int>> Targets);

    private async Task<CatchUpFacts> CatchUpFactsAsync(IReadOnlyList<int> ids, CancellationToken ct)
    {
        var posts = await db.PostParserTasks.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var runIds = posts.Values.Where(p => p.WorkflowRunId != null).Select(p => p.WorkflowRunId!.Value).Distinct().ToList();
        var runs = await db.WorkflowRuns.AsNoTracking().Where(r => runIds.Contains(r.Id))
            .Select(r => new {r.Id, r.Status}).ToDictionaryAsync(r => r.Id, r => r.Status, ct);
        var sourceIds = ids.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToList();
        var handoffs = await db.AcquisitionHandoffs.AsNoTracking().Where(h => h.SourceModule == "postParser" &&
            h.SourceKind == "task" && sourceIds.Contains(h.SourceId)).ToListAsync(ct);
        var grouped = handoffs.GroupBy(h => (h.SourceId, h.SourceRevision)).ToList();
        return new(posts, runs, grouped.ToDictionary(g => g.Key,
                g => g.OrderByDescending(h => h.UpdatedAt).ThenBy(h => h.Id, StringComparer.Ordinal).First()),
            grouped.Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(),
            await ExistingPostTasksAsync(ct));
    }

    private PostAcquisitionCatchUpItem CatchUpItem(int id, CatchUpFacts facts)
    {
        if (!facts.Posts.TryGetValue(id, out var post) || post.IsDeleted)
            return new(id, null, "rejected", "HANDOFF_SOURCE_UNAVAILABLE", null, null, []);
        var key = (post.Id.ToString(CultureInfo.InvariantCulture), post.Revision.ToString(CultureInfo.InvariantCulture));
        var handoff = facts.Handoffs.GetValueOrDefault(key);
        var targets = facts.Targets.GetValueOrDefault(key) ?? [];
        var ready = SavedResultReady(post);
        var producerReady = post.WorkflowRunId is not { } runId ||
            !facts.Runs.TryGetValue(runId, out var status) || status == WorkflowRunStatus.Success;
        var selection = ready && producerReady ? new PostAcquisitionCatchUpSelection(id, post.Revision,
            post.WorkflowRunId, ResultsHash(post)) : null;
        if (handoff != null || targets.Count > 0)
            return new(id, post.Title, "existing", facts.DuplicateHandoffs.Contains(key) ? "HANDOFF_MULTIPLE_RECORDS" :
                handoff?.Code ?? "HANDOFF_EXISTING_TASK", selection,
                handoff?.Id, targets, post.Link);
        var problem = options.Value.PostParserAcquisition?.Enabled != true ? "HANDOFF_DISABLED" :
            !ready ? "HANDOFF_SOURCE_NOT_READY" : !producerReady ? "HANDOFF_PRODUCER_NOT_SUCCESSFUL" : null;
        return new(id, post.Title, problem == null ? "eligible" : "rejected", problem,
            problem == null ? selection : null, null, [], post.Link);
    }

    private static bool SavedResultReady(PostParserTaskDbModel post)
    {
        try
        {
            return (post.Error == null || post.Error == PostParserSavedResults.MissingWorkflowRunError) &&
                   PostParserSavedResults.HasCompletedDownloadResult(post);
        }
        catch (Exception error) when (error is JsonException or Newtonsoft.Json.JsonException)
        {return false;}
    }

    private static string ResultsHash(PostParserTaskDbModel post) =>
        PostAcquisitionResultFingerprint.Hash(post.Results);

    // This content-addressed ID is the durable saved-result receipt. It is deliberately
    // distinct from a workflow-completion event: a cleaned-up producer run is not recreated
    // or asserted successful. Source-tuple uniqueness remains the shared deduplication key.
    private static string SavedResultId(PostParserTaskDbModel post) => SavedResultIdPrefix +
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            Module = "postParser", Kind = "task", post.Id, post.Revision, post.WorkflowRunId,
            ResultsHash = ResultsHash(post)
        }))));

    private async Task<Dictionary<(string Id, string Revision), List<int>>> ExistingPostTasksAsync(CancellationToken ct)
    {
        var result = new Dictionary<(string Id, string Revision), List<int>>();
        var rows = await db.AcquisitionTasks.AsNoTracking().Where(t => t.SourceRefsJson != null)
            .Select(t => new {t.Id, t.SourceRefsJson}).ToListAsync(ct);
        foreach (var row in rows)
        {
            List<WorkflowSourceRef>? refs;
            try {refs = JsonSerializer.Deserialize<List<WorkflowSourceRef>>(row.SourceRefsJson!, JsonSerializerOptions.Web);}
            catch (JsonException) {continue;}
            foreach (var source in (refs ?? []).Where(s => s != null && s.Module == "postParser" &&
                         s.EntityKind == "task" && s.Revision != null).DistinctBy(s => (s.EntityId, s.Revision)))
            {
                var key = (source.EntityId, source.Revision!);
                if (!result.TryGetValue(key, out var targets)) result[key] = targets = [];
                targets.Add(row.Id);
            }
        }
        return result;
    }
}
