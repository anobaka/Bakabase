using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Services;

public partial class PostParserTaskService<TDbContext>
{
    public async Task<PostParserTaskDeletionPreview> PreviewDeletion(PostParserTaskFilterScope scope,
        CancellationToken ct = default)
    {
        var normalized = ValidateFilterScope(scope);
        var facts = await ReadFacts(normalized.Keyword!, ct);
        var selected = facts.Where(f => MatchesScope(f, normalized)).ToList();
        if (selected.Count > 10_000)
            throw new ArgumentException("Narrow the deletion filter to at most 10000 tasks.");
        return new()
        {
            Scope = normalized, Count = selected.Count,
            Tasks = selected.Select(f => new PostParserTaskDeletionSelection
                {TaskId = f.Id, Revision = f.Revision, WorkflowRunId = f.WorkflowRunId}).ToList()
        };
    }

    public async Task<PostParserTaskDeletionResult> ConfirmDeletion(ConfirmPostParserTaskDeletionInput input,
        CancellationToken ct = default)
    {
        var scope = ValidateFilterScope(input.Scope);
        if (input.Tasks == null || input.Tasks.Count > 10_000 ||
            input.Tasks.Any(t => t == null || t.TaskId <= 0 || t.Revision < 0 || t.Revision == int.MaxValue || t.WorkflowRunId is <= 0) ||
            input.Tasks.Select(t => t.TaskId).Distinct().Count() != input.Tasks.Count)
            throw new ArgumentException("Provide at most 10000 unique task snapshots with valid identifiers and revisions.");

        var result = new PostParserTaskDeletionResult {RequestedCount = input.Tasks.Count};
        var stoppedRuns = new List<int>();
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Use the same current state/keyword rules as paging. The fixed client manifest is
            // only intersected with current matches; it must never turn into a new bulk query.
            var eligible = (await ReadFacts(scope.Keyword!, ct)).Where(f => MatchesScope(f, scope)).ToDictionary(f => f.Id);
            var ids = input.Tasks.Select(t => t.TaskId).ToList();
            var rows = await ParserTasks.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
            var changed = new List<PostParserTaskDbModel>();
            foreach (var snapshot in input.Tasks)
            {
                if (!rows.TryGetValue(snapshot.TaskId, out var row) || row.IsDeleted)
                {
                    result.Items.Add(new(snapshot.TaskId, "skipped", "missingOrDeleted"));
                    continue;
                }
                if (row.Revision != snapshot.Revision || row.WorkflowRunId != snapshot.WorkflowRunId)
                {
                    result.Items.Add(new(snapshot.TaskId, "skipped", "snapshotChanged"));
                    continue;
                }
                if (!eligible.ContainsKey(row.Id))
                {
                    result.Items.Add(new(snapshot.TaskId, "skipped", "scopeChanged"));
                    continue;
                }
                var updated = await ParserTasks.Where(t => t.Id == row.Id && !t.IsDeleted &&
                        t.Revision == snapshot.Revision && t.WorkflowRunId == snapshot.WorkflowRunId)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDeleted, true)
                        .SetProperty(t => t.Revision, t => t.Revision + 1), ct);
                if (updated != 1)
                {
                    result.Items.Add(new(snapshot.TaskId, "skipped", "snapshotChanged"));
                    continue;
                }
                row.IsDeleted = true;
                row.Revision++;
                changed.Add(row);
                if (row.WorkflowRunId is { } runId) stoppedRuns.Add(runId);
                result.Items.Add(new(row.Id, "deleted"));
            }
            await CancelRunsUnderGate(stoppedRuns);
            await transaction.CommitAsync(ct);
            cache.ClearCache();
            result.DeletedCount = changed.Count;
            result.SkippedCount = result.RequestedCount - result.DeletedCount;
            foreach (var row in changed)
            {
                try { await Publish(row); }
                catch (Exception error) { logger.LogWarning(error, "Could not publish deleted parser task {TaskId}.", row.Id); }
            }
        }
        finally { gate.Semaphore.Release(); }
        // Only handlers belonging to the committed selection are stopped, never a new match.
        // The tombstone/revision already prevents late results from publishing to these tasks.
        foreach (var id in stoppedRuns.Distinct())
        {
            try { await tasks.Stop($"workflow.run.{id}"); }
            catch (Exception error) { logger.LogWarning(error, "Could not stop deleted parser workflow {RunId}.", id); }
        }
        return result;
    }

    private static PostParserTaskFilterScope ValidateFilterScope(PostParserTaskFilterScope scope)
    {
        if (scope == null || scope.Keyword?.Length > 1000 ||
            scope.Status is not (null or "all") && !StatusGroups.Contains(scope.Status) ||
            scope.ReasonCode != null && !PostParserAttentionReasons.Known.Contains(scope.ReasonCode))
            throw new ArgumentException("Provide a bounded keyword and valid task status and reason filters.");
        return new() {Keyword = scope.Keyword?.Trim() ?? "", Status = scope.Status ?? "all", ReasonCode = scope.ReasonCode};
    }
}
