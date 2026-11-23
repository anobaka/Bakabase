using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>A task can offer a different continuation when its provider recognizes the retained evidence.</summary>
public interface IAcquisitionBatchActionProvider
{
    string Action { get; }
    Task<AcquisitionBatchActionOffer?> PreviewAsync(AcquisitionTaskDbModel task, WorkflowRunDbModel? run,
        int maxAdaptiveIterations, CancellationToken ct);
}

public abstract record AcquisitionBatchActionOffer;
public sealed record AcquisitionBatchWorkflowActionOffer(AcquisitionRecoveryDiagnosis Diagnosis) : AcquisitionBatchActionOffer;
public sealed record AcquisitionBatchFileActionOffer(string Fingerprint, bool UseCurrentStorage, string ReadyReason) : AcquisitionBatchActionOffer;

public interface IAcquisitionBatchFileActionProvider : IAcquisitionBatchActionProvider
{
    Task<(int WorkflowRunId, int PlanRevision)> ApplyAsync(int taskId, string fingerprint, int maxAdaptiveIterations,
        CancellationToken ct, Func<string?, Task>? guardDirectory);
}

internal sealed class AcquisitionTransferReplayAction(IAcquisitionRecoveryDiagnostics diagnostics) : IAcquisitionBatchActionProvider
{
    internal const string Id = "retryTransfer";
    public string Action => Id;

    public async Task<AcquisitionBatchActionOffer?> PreviewAsync(AcquisitionTaskDbModel task, WorkflowRunDbModel? run,
        int maxAdaptiveIterations, CancellationToken ct) => new AcquisitionBatchWorkflowActionOffer(
        await diagnostics.DiagnoseTransferRetryAsync(task, run, ct));
}

internal sealed class AcquisitionReanalysisAction(AcquisitionAdaptiveMigrationService adaptive) : IAcquisitionBatchFileActionProvider
{
    public string Action => "reanalyze";

    public async Task<AcquisitionBatchActionOffer?> PreviewAsync(AcquisitionTaskDbModel task, WorkflowRunDbModel? run,
        int maxAdaptiveIterations, CancellationToken ct) =>
        await adaptive.PreviewForBatchAsync(task, run, maxAdaptiveIterations, ct) is { } plan
            ? new AcquisitionBatchFileActionOffer(plan.Fingerprint, plan.UseCurrentStorage, "ADAPTIVE_MIGRATION_READY") : null;

    public Task<(int WorkflowRunId, int PlanRevision)> ApplyAsync(int taskId, string fingerprint, int maxAdaptiveIterations,
        CancellationToken ct, Func<string?, Task>? guardDirectory) =>
        adaptive.ApplyForBatchAsync(taskId, fingerprint, maxAdaptiveIterations, ct, guardDirectory);
}
