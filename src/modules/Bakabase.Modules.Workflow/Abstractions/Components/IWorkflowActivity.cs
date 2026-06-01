using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>
/// One pluggable step. Implementations register as singletons; the registry indexes them
/// by <see cref="Kind"/>. The runner picks up an instance per configured activity row and
/// drives it via <see cref="ProcessItemAsync"/>.
///
/// <para><b>Typed item flow.</b> Items carry a semantic type tag (an opaque string, e.g.
/// "item.exhentai.gallery"). The editor and <c>WorkflowDefinitionService</c> walk the chain
/// left-to-right tracking the "current type": the trigger emits a starting type, then each
/// activity must accept the current type and may change it. This is how a Pixiv-sourced chain
/// refuses an ExHentai-specific action unless a transform bridges the types in between.</para>
/// </summary>
public interface IWorkflowActivity
{
    /// <summary>Stable identifier — built via <see cref="WorkflowActivityKinds"/>.</summary>
    string Kind { get; }

    /// <summary>Human-readable name for the activity picker.</summary>
    string DisplayName { get; }

    /// <summary>UI bucketing (Filter / Action / Transform).</summary>
    WorkflowActivityCategory Category { get; }

    /// <summary>
    /// Free-form group tag used by the editor's picker to bucket activities (e.g.
    /// "exhentai", "pixiv", "ai", "subscription"). The frontend maps known groups to
    /// icon + label (e.g. ThirdPartyLabel for "exhentai" / "pixiv"); unknown groups render
    /// as plain text.
    /// </summary>
    string Group { get; }

    /// <summary>
    /// Item types this activity can consume. Empty = accepts any type (generic filters /
    /// side-effect actions that don't care about source). A concrete type matches only when
    /// it appears in this list. The "any" sentinel a trigger may emit for a heterogeneous
    /// feed never matches a non-empty list — that's deliberate; source-specific activities
    /// must be fed a pinned type.
    /// </summary>
    IReadOnlyList<string> AcceptedInputItemTypes => [];

    /// <summary>How the activity affects the chain's item type at this position.</summary>
    WorkflowItemTypeBehavior OutputBehavior => WorkflowItemTypeBehavior.Passthrough;

    /// <summary>The fixed output type when <see cref="OutputBehavior"/> is
    /// <see cref="WorkflowItemTypeBehavior.Fixed"/>; otherwise null.</summary>
    string? FixedOutputItemType => null;

    /// <summary>
    /// For <see cref="WorkflowItemTypeBehavior.AdaptToNext"/> activities, resolve the
    /// effective output type given this activity's config and a hint about what the next
    /// activity needs. Return null when the type can't be decided (the editor flags the
    /// activity invalid). Default: just adopt <paramref name="nextActivityRequiredType"/>.
    /// </summary>
    string? ResolveAdaptedOutputType(string configJson, string? nextActivityRequiredType)
        => nextActivityRequiredType;

    /// <summary>
    /// Process one item from the chain.
    /// <list type="bullet">
    ///   <item>Filter: return <see cref="WorkflowItemOutcome.KeepItem"/> or <see cref="WorkflowItemOutcome.DropItem"/>.</item>
    ///   <item>Action: do the side effect, return <see cref="WorkflowItemOutcome.KeepItem"/>.</item>
    ///   <item>Transform: return <see cref="WorkflowItemOutcome.ReplaceWith"/> or <see cref="WorkflowItemOutcome.DropItem"/>.</item>
    /// </list>
    /// Throwing is treated by the runner per the activity's <c>OnItemError</c> setting.
    /// </summary>
    Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item, CancellationToken ct);
}
