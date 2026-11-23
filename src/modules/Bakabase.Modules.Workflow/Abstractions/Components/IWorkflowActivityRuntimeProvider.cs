using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>Owner-supplied, bounded read of an activity's existing runtime facts. Scoped providers
/// may read local storage or an existing status API asynchronously, honoring cancellation and
/// their own read budgets/cache. Never start work, probe a source, or change execution policy.</summary>
public interface IWorkflowActivityRuntimeProvider
{
    bool CanHandle(string activityKind);
    ValueTask<WorkflowActivityRuntimeSnapshot> ReadAsync(WorkflowActivityRuntimeContext context,
        CancellationToken ct = default);
}

/// <summary>Typed in-process evidence supplied by a capability's reader; never a client payload.</summary>
public interface IWorkflowActivityRuntimeEvidence { }

/// <summary>Measured facts from an independently managed transfer. Unknown counters stay null.</summary>
public record WorkflowRuntimeTransferSnapshot(string Id, string? ParentId, int? WorkflowRunId,
    string Status, double? Progress, long? CompletedBytes, long? TotalBytes, DateTimeOffset ObservedAt,
    string? ProblemCode, double? SpeedBytesPerSecond = null, string? Stage = null);

public sealed record WorkflowActivityRuntimeContext(int WorkflowRunId, int? StepIndex, string ActivityKind,
    WorkflowRunStatus Status, WorkflowTaskProgress? LiveProgress,
    IReadOnlyList<IWorkflowActivityRuntimeEvidence> Evidence)
{
    public WorkflowTaskProgress? CurrentProgress => StepIndex != null && Status == WorkflowRunStatus.Running &&
        LiveProgress is { } live && live.WorkflowRunId == WorkflowRunId && live.StepIndex == StepIndex &&
        live.ActivityKind == ActivityKind ? live : null;
}

/// <summary>Only report queue state and limits when the owning capability can observe them.</summary>
public sealed record WorkflowCapabilityRuntime(string Id, string DisplayName, string? DisplayNameKey = null,
    string? Status = null, string? WaitReason = null, string? ExecutionPolicy = null, int? MaximumParallelism = null);

public sealed record WorkflowActivityRuntimeSnapshot
{
    public WorkflowCapabilityRuntime? Capability { get; init; }
    public string? Stage { get; init; }
    public double? Progress { get; init; }
    public string? ProgressScope { get; init; }
    public long? CompletedBytes { get; init; }
    public long? TotalBytes { get; init; }
    public double? SpeedBytesPerSecond { get; init; }
    public long? EstimatedRemainingSeconds { get; init; }
}
