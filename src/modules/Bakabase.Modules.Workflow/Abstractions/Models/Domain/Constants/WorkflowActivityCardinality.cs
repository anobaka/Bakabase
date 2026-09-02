namespace Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

/// <summary>
/// How many items an activity may hand to the next step per input item (capability map E2).
/// The editor uses it to explain 1→N steps; the runner uses it to refuse an
/// <c>ExpandTo</c> outcome from an activity that never declared one.
/// </summary>
public enum WorkflowActivityCardinality
{
    OneToOne = 1,

    /// <summary>The activity may replace one item with any number of items (0..N) via
    /// <c>WorkflowItemOutcome.ExpandTo</c> — a directory into its children, a gallery into
    /// its images. Step stats' independent input/output counts express the fan-out.</summary>
    OneToMany = 2,
}
