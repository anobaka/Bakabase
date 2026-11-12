using System.Runtime.CompilerServices;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

[assembly: InternalsVisibleTo("Bakabase.Tests")]

namespace Bakabase.Modules.Workflow.Components;

/// <summary>Only records a stopped execution after its final connection became unusable.
/// Never imports uncommitted items, outputs, nodes or side effects into another context.</summary>
internal static class WorkflowCheckpointFailureRecovery
{
    internal const string FailureMessage = "Workflow checkpoint could not be saved. Execution stopped; review the last saved step before retrying.";
    internal sealed record Guard(WorkflowRunDbModel Run, int NodeCount, long LastNodeId,
        string? LastNodeStatus, string? LastNodeOutputs, DateTime? LastNodeCompletedAt,
        bool LastNodeOutputsTruncated, string? LastNodeIdentity, string? LastNodeKind,
        int LastNodeAttempt, DateTime? LastNodeStartedAt);

    internal static Guard Capture(WorkflowRunDbModel run)
    {
        var last = run.NodeExecutions.MaxBy(n => n.Id);
        return new(run with {Sources = [], NodeExecutions = []}, run.NodeExecutions.Count,
            last?.Id ?? 0, last?.Status, last?.OutputsJson, last?.CompletedAt, last?.OutputsTruncated ?? false,
            last?.NodeId, last?.Kind, last?.Attempt ?? 0, last?.StartedAt);
    }

    private static bool IsDisposed(Exception error) => error is ObjectDisposedException ||
        error.InnerException is { } inner && IsDisposed(inner);

    internal static async Task SaveFinalAsync<TDbContext>(TDbContext original, IServiceScopeFactory scopes,
        Guard durable, ILogger logger) where TDbContext : DbContext
    {
        try { await original.SaveChangesAsync(CancellationToken.None); }
        catch (Exception error) when (IsDisposed(error))
        {
            try
            {
                var repaired = await MarkInterruptedAsync<TDbContext>(scopes, durable);
                logger.LogWarning("Workflow run {RunId} final checkpoint failed; unchanged durable run marked interrupted: {Repaired}",
                    durable.Run.Id, repaired);
            }
            catch (Exception recoveryError)
            {
                // The primary failure remains the thrown exception. Never put database/error
                // payloads into the task row or allow fallback persistence to trigger a replay.
                logger.LogWarning("Workflow run {RunId} checkpoint failure status could not be recorded ({ErrorType})",
                    durable.Run.Id, recoveryError.GetType().Name);
            }
            throw;
        }
    }

    private static async Task<bool> MarkInterruptedAsync<TDbContext>(IServiceScopeFactory scopes, Guard durable)
        where TDbContext : DbContext
    {
        var saved = durable.Run;
        if (saved.Status != WorkflowRunStatus.Running) return false;
        await using var scope = scopes.CreateAsyncScope();
        var fresh = scope.ServiceProvider.GetRequiredService<TDbContext>();
        // A single conditional UPDATE is the concurrency boundary. If the failed write
        // actually committed, or another execution advanced, preserve that newer state.
        var rows = fresh.Set<WorkflowRunDbModel>().Where(r => r.Id == saved.Id &&
            r.Status == WorkflowRunStatus.Running && r.StartedAt == saved.StartedAt &&
            r.WorkflowDefinitionId == saved.WorkflowDefinitionId && r.PlanSnapshotJson == saved.PlanSnapshotJson &&
            r.PayloadJson == saved.PayloadJson && r.PayloadSummary == saved.PayloadSummary &&
            r.CurrentStepIndex == saved.CurrentStepIndex && r.CurrentItemJson == saved.CurrentItemJson &&
            r.PendingSignalJson == saved.PendingSignalJson && r.StepStatsJson == saved.StepStatsJson &&
            r.WaitReason == saved.WaitReason && r.WaitPromptJson == saved.WaitPromptJson && r.WaitingSince == saved.WaitingSince &&
            r.InputCount == saved.InputCount && r.OutputCount == saved.OutputCount && r.OutputItemsJson == saved.OutputItemsJson &&
            r.OutputPreviewTruncated == saved.OutputPreviewTruncated && r.FailedItemCount == saved.FailedItemCount &&
            r.CompletedAt == saved.CompletedAt && r.ErrorMessage == saved.ErrorMessage &&
            r.NodeExecutions.Count == durable.NodeCount &&
            !r.NodeExecutions.Any(n => n.Id > durable.LastNodeId) &&
            (durable.LastNodeId == 0 || r.NodeExecutions.Any(n => n.Id == durable.LastNodeId &&
                n.Status == durable.LastNodeStatus && n.OutputsJson == durable.LastNodeOutputs &&
                n.OutputsTruncated == durable.LastNodeOutputsTruncated && n.NodeId == durable.LastNodeIdentity &&
                n.Kind == durable.LastNodeKind && n.Attempt == durable.LastNodeAttempt && n.StartedAt == durable.LastNodeStartedAt &&
                n.CompletedAt == durable.LastNodeCompletedAt)));
        return await rows.ExecuteUpdateAsync(set => set
            .SetProperty(r => r.Status, WorkflowRunStatus.Interrupted)
            .SetProperty(r => r.CompletedAt, DateTime.Now)
            .SetProperty(r => r.ErrorMessage, FailureMessage), CancellationToken.None) == 1;
    }
}
