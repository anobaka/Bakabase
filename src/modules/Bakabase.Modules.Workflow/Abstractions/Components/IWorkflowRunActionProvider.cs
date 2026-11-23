using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>
/// A read-only view of a workflow run that a capability can use to advertise
/// operator actions. Providers must not mutate the run while describing actions.
/// The node identity is stable for the frozen run plan; it is deliberately not
/// represented by a positional step index.
/// </summary>
public sealed record WorkflowRunActionContext(
    WorkflowRun Run,
    string? CurrentNodeId = null,
    string? CurrentActivityKind = null,
    IReadOnlyList<IWorkflowActivityRuntimeEvidence>? Evidence = null)
{
    public IReadOnlyList<IWorkflowActivityRuntimeEvidence> RuntimeEvidence { get; } = Evidence ?? [];
}

/// <summary>The kind of change an advertised action performs when it is applied.</summary>
public enum WorkflowRunActionMode
{
    Continue = 1,
    Retry = 2,
    Recompute = 3,
}

/// <summary>
/// A capability-owned operation that can be offered for a selected run. The
/// descriptor is safe to send to a client; execution inputs and preview tokens
/// remain provider-owned.
/// </summary>
public sealed record WorkflowRunActionDescriptor(
    string Id,
    string CapabilityId,
    WorkflowRunActionMode Mode,
    string DisplayName,
    string? DisplayNameKey = null,
    string? NodeId = null,
    bool RequiresPreview = true,
    bool RequiresInput = false,
    string? DisabledReason = null);

/// <summary>
/// Capability-owned discovery for workflow recovery actions. A provider may
/// inspect its own persisted evidence asynchronously and may return no actions
/// when the current run is outside its safe recovery boundary.
/// </summary>
public interface IWorkflowRunActionProvider
{
    bool CanHandle(WorkflowRunActionContext context);

    ValueTask<IReadOnlyList<WorkflowRunActionDescriptor>> DescribeAsync(
        WorkflowRunActionContext context, CancellationToken ct = default);
}
