namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>
/// One Activity's verdict on a single item.
/// </summary>
public readonly record struct WorkflowItemOutcome(
    bool Keep,
    object? Replacement = null,
    IReadOnlyList<object>? Children = null)
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
}
