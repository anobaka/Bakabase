namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>
/// An event source that can fire workflow definitions. One per kind of event in the system.
/// </summary>
public interface IWorkflowTrigger
{
    /// <summary>Stable identifier — built via <see cref="WorkflowTriggerKinds.Build"/>.</summary>
    string Kind { get; }

    /// <summary>Human-readable name for the trigger picker in the editor.</summary>
    string DisplayName { get; }

    /// <summary>CLR type of the payload this trigger publishes. Activities can cast against it.</summary>
    Type PayloadType { get; }

    /// <summary>
    /// Decide whether a given event should activate a workflow definition based on its
    /// trigger filter (opaque JSON owned by this trigger's UI).
    /// </summary>
    bool Matches(object payload, string? triggerFilterJson);

    /// <summary>
    /// Extract the initial item list from the event payload. The runner uses this as
    /// the starting <c>ctx.Items</c> for the activity chain.
    /// </summary>
    IReadOnlyList<object> ExtractItems(object payload);

    /// <summary>
    /// The semantic item type this trigger emits, given a definition's trigger filter.
    /// May depend on the filter (e.g. a subscription trigger pinned to ExHentai kinds emits
    /// "item.exhentai.gallery", while an unpinned one emits a generic "any" tag). Drives the
    /// typed-flow validation in the editor and service. Must be a pure function of the filter
    /// — the editor mirrors it client-side.
    /// </summary>
    string ResolveOutputItemType(string? triggerFilterJson);
}
