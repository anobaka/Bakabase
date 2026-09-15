namespace Bakabase.Modules.Acquisition.Components.Workflow;

/// <summary>
/// The names acquisition uses inside the workflow engine. They follow the engine's own grammar so a
/// recipe looks like any other workflow in the editor — which it is.
/// </summary>
public static class AcquisitionWorkflowKinds
{
    /// <summary>
    /// A recipe's trigger. It never matches an event: a recipe runs because someone asked for a
    /// resource, and that ask arrives through <c>IAcquisitionService</c> as a manual run.
    /// </summary>
    public const string TriggerRequested = "acquisition.requested";

    /// <summary>
    /// Fires when an acquisition changes state, so the user's own workflows can react to one.
    /// </summary>
    public const string TriggerStatusChanged = "acquisition.statusChanged";

    /// <summary>The one item type flowing through a recipe.</summary>
    public const string ItemAcquisition = "item.acquisition";

    /// <summary>What <see cref="TriggerStatusChanged"/> emits.</summary>
    public const string ItemStatusChange = "item.acquisition.statusChange";

    /// <summary>The activity picker's bucket for the acquisition steps.</summary>
    public const string ActivityGroup = "acquisition";
}
