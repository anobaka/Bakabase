namespace Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

/// <summary>
/// How an activity affects the item TYPE flowing through the chain (distinct from whether it
/// keeps/drops individual items at runtime).
/// </summary>
public enum WorkflowItemTypeBehavior
{
    /// <summary>Output items are the same type as input — filters and side-effect actions.</summary>
    Passthrough = 1,

    /// <summary>Output items are a fixed declared type regardless of input — transforms.</summary>
    Fixed = 2,

    /// <summary>
    /// Output type is resolved per-chain by the activity itself (typically a generic AI
    /// transform): it may consult its own config for a user-pinned target type, or fall back
    /// to whatever the next activity in the chain accepts. The chain walker calls
    /// <c>IWorkflowActivity.ResolveAdaptedOutputType</c>; null means "can't decide" and the
    /// editor flags the activity invalid.
    /// </summary>
    AdaptToNext = 3,
}
