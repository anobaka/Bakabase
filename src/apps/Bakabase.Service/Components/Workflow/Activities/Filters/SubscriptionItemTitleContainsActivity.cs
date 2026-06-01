using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain;
using Bakabase.Modules.Subscription.Workflow;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

namespace Bakabase.Service.Components.Workflow.Activities.Filters;

/// <summary>
/// Keeps <see cref="SubscriptionItem"/>s whose title contains any of the configured
/// keywords (case-insensitive). When <see cref="Config.Exclude"/> is true, the predicate
/// is inverted (drop matches, keep non-matches).
/// </summary>
public class SubscriptionItemTitleContainsActivity : IWorkflowActivity
{
    public string Kind { get; } = SubscriptionWorkflowActivityKinds.FilterItemTitleContains;
    public string DisplayName => "Filter by item title";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Filter;
    public string Group => WorkflowActivityGroups.Subscription;
    // Title is present on every subscription item shape → accept any type, pass it through.
    // (AcceptedInputItemTypes / OutputBehavior use the interface defaults: any / passthrough.)

    public Task<WorkflowItemOutcome> ProcessItemAsync(
        WorkflowExecutionContext ctx, object item, CancellationToken ct)
    {
        if (item is not SubscriptionItem si)
        {
            return Task.FromResult(WorkflowItemOutcome.KeepItem);
        }

        var cfg = ctx.GetConfig<Config>() ?? new Config();
        var keywords = cfg.Keywords?.Where(k => !string.IsNullOrWhiteSpace(k)).ToArray() ?? [];
        if (keywords.Length == 0)
        {
            // No keywords configured → behave as a no-op (don't accidentally drop everything).
            return Task.FromResult(WorkflowItemOutcome.KeepItem);
        }

        var title = si.Title ?? string.Empty;
        var matchAny = keywords.Any(k => title.Contains(k, StringComparison.OrdinalIgnoreCase));
        var shouldKeep = cfg.Exclude ? !matchAny : matchAny;

        return Task.FromResult(shouldKeep ? WorkflowItemOutcome.KeepItem : WorkflowItemOutcome.DropItem);
    }

    private sealed record Config
    {
        public string[]? Keywords { get; init; }

        /// <summary>When true, items that match are dropped instead of kept.</summary>
        public bool Exclude { get; init; }
    }
}
