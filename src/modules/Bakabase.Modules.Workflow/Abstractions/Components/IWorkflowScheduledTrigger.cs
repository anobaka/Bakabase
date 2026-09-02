namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>
/// A trigger that also fires on the clock (capability map E6). The scheduler sweeps enabled
/// definitions whose trigger implements this, asks each definition's own filter for its
/// interval, and starts a run when one is due — the schedule is per-definition data, not
/// per-trigger code, so one trigger kind serves every cadence.
/// </summary>
public interface IWorkflowScheduledTrigger : IWorkflowTrigger
{
    /// <summary>
    /// The definition's configured interval, or null when the filter has no (valid) schedule —
    /// a null keeps the definition manual-only without failing anything.
    /// </summary>
    TimeSpan? GetInterval(string? triggerFilterJson);
}
