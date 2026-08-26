using System.Text.Json;

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

    /// <summary>
    /// Whether starting a run by hand needs the user to supply a payload.
    ///
    /// True for an event trigger: running one manually is a replay, and something has to stand
    /// in for the event that never happened. A trigger whose inputs are fully described by the
    /// definition's own configuration — a scan over configured roots, say — returns false, and
    /// its <see cref="BuildManualPayload"/> ignores the args entirely.
    /// </summary>
    bool RequiresManualPayload => true;

    /// <summary>
    /// Build the payload for a manual run. The default reads it from what the user typed, which
    /// is what makes every event trigger debuggable without each one writing code for it;
    /// triggers that carry their parameters on the definition override this and build from
    /// <paramref name="triggerFilterJson"/> instead.
    ///
    /// Throws <see cref="InvalidOperationException"/> with a message meant for the user — this
    /// runs inside the request that asked for the run, so a bad payload is reported there rather
    /// than becoming a failed run to go looking for.
    /// </summary>
    object BuildManualPayload(string? triggerFilterJson, string? argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson))
        {
            throw new InvalidOperationException(
                $"Trigger [{Kind}] needs a payload to be run manually.");
        }

        try
        {
            return JsonSerializer.Deserialize(argsJson, PayloadType, WorkflowJson.Options)
                   ?? throw new InvalidOperationException("The payload deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"The payload does not match {PayloadType.Name}: {ex.Message}", ex);
        }
    }
}
