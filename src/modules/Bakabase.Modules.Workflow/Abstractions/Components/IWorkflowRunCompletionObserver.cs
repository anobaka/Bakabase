using Bakabase.Modules.Workflow.Abstractions.Models.Db;

namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>
/// Stages durable follow-up intents in the runner's scoped unit of work immediately before
/// a successful run is committed. Implementations must not perform network/file work, execute
/// downstream workflows or call SaveChanges. Waiting/failed runs and historical reads do not notify.
/// </summary>
public interface IWorkflowRunCompletionObserver
{
    Task StageAsync(WorkflowRunDbModel run, object payload, CancellationToken ct);
}
