namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>
/// An activity stopping to wait for something from outside the chain — a file only a person can
/// fetch, a password, a decision. The run parks in <c>Waiting</c> until a signal arrives.
/// </summary>
/// <param name="Reason">Why it is waiting; shown in the runs list and used to pick the form to ask with.</param>
/// <param name="PromptJson">What the interface should ask for. Shape is the activity's own business.</param>
/// <param name="Item">The item as it stands, snapshotted so the wait can outlive the process.</param>
public record WorkflowSuspension(string Reason, string? PromptJson, object Item);

/// <summary>
/// One Activity's verdict on a single item.
/// </summary>
public readonly record struct WorkflowItemOutcome(
    bool Keep,
    object? Replacement = null,
    IReadOnlyList<object>? Children = null,
    WorkflowSuspension? Suspension = null)
{
    /// <summary>Pass the item through to the next activity unchanged.</summary>
    public static readonly WorkflowItemOutcome KeepItem = new(true);

    /// <summary>Remove this item from the chain.</summary>
    public static readonly WorkflowItemOutcome DropItem = new(false);

    /// <summary>Pass through, but replace the item value (Transform activities).</summary>
    public static WorkflowItemOutcome ReplaceWith(object replacement) => new(true, replacement);

    /// <summary>
    /// Replace this item with zero or more items (capability map E2) — a directory becomes its
    /// children, a gallery its images. Only activities declaring
    /// <c>Cardinality = OneToMany</c> may return this; each child inherits a copy of the
    /// parent's variable bag. An empty list is a legal way to say "expanded to nothing".
    /// </summary>
    public static WorkflowItemOutcome ExpandTo(IReadOnlyList<object> children) =>
        new(true, null, children);

    /// <summary>
    /// Stop and wait for a signal. Only meaningful on a run carrying a single item — a batch has
    /// no sensible answer to "which item is the user being asked about", so the runner treats a
    /// suspension there as a configuration error.
    /// </summary>
    public static WorkflowItemOutcome Suspend(WorkflowSuspension suspension) =>
        new(true, null, null, suspension);
}
