using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Service.Components.Workflow.Activities;

/// <summary>
/// Activity kinds that operate on <see cref="SubscriptionItem"/>s. These are generic enough
/// (a filter on item title applies to every subscription kind) so they live under the
/// "subscription" module namespace rather than per-source.
/// </summary>
public static class SubscriptionWorkflowActivityKinds
{
    private const string Module = "subscription";

    public static readonly string FilterItemTitleContains =
        WorkflowActivityKinds.Filter(Module, "itemTitleContains");
}
