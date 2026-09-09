namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>
/// An activity that can stop and be started again with an answer.
/// <para>
/// Opting in is the whole contract: an activity that never returns
/// <see cref="WorkflowItemOutcome.Suspend"/> is unaffected by any of this, and one that does but
/// does not implement this interface fails the run rather than leaving it parked forever with
/// nothing able to wake it.
/// </para>
/// </summary>
public interface IResumableWorkflowActivity : IWorkflowActivity
{
    /// <summary>
    /// Continues from the suspension. The item is the snapshot taken when it suspended, so an
    /// activity sees exactly what it left behind, however much later this is.
    /// </summary>
    /// <param name="signalJson">The answer, shaped by whatever the suspension's prompt asked for.</param>
    Task<WorkflowItemOutcome> ResumeAsync(WorkflowExecutionContext ctx, object item, string signalJson,
        CancellationToken ct);
}
