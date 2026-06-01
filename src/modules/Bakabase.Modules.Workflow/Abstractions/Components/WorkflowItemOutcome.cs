namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>
/// One Activity's verdict on a single item.
/// </summary>
public readonly record struct WorkflowItemOutcome(bool Keep, object? Replacement = null)
{
    /// <summary>Pass the item through to the next activity unchanged.</summary>
    public static readonly WorkflowItemOutcome KeepItem = new(true);

    /// <summary>Remove this item from the chain.</summary>
    public static readonly WorkflowItemOutcome DropItem = new(false);

    /// <summary>Pass through, but replace the item value (Transform activities).</summary>
    public static WorkflowItemOutcome ReplaceWith(object replacement) => new(true, replacement);
}
