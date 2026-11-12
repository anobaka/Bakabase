using System.Collections.Concurrent;

namespace Bakabase.Modules.Workflow.Components;

/// <summary>
/// Serializes resuming a stopped run with its owner's removal. A caller needing both locks
/// must take its business-task gate first, then this run gate; never the reverse order.
/// Running work is not suspended by this gate and must be excluded by the removal guard.
/// </summary>
public static class WorkflowRunMutationGate
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> Gates = new();
    public static SemaphoreSlim For(int runId) => Gates.GetOrAdd(runId, _ => new(1, 1));
}
