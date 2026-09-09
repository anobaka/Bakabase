using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Acquisition.Components.Workflow;

/// <summary>
/// Starts an acquisition from inside another workflow.
/// <para>
/// This is the outer half of Workflow's two roles: a recipe is a workflow, and a workflow can also
/// ask for one. "Circle X released something → make a placeholder → go and get it" is a chain the
/// user builds, not a feature anyone has to write.
/// </para>
/// </summary>
public class AcquisitionCreateActivity : IWorkflowActivity
{
    public string Kind => "action.acquisition.create";
    public string DisplayName => "Start acquiring";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Action;
    public string Group => AcquisitionWorkflowKinds.ActivityGroup;

    /// <summary>
    /// Anything that names a resource. The contract keeps this out of the business of knowing which
    /// triggers exist — an item is acceptable if it can say which resource it is about.
    /// </summary>
    public Type? AcceptedItemInterface => typeof(IHasResourceId);

    public WorkflowItemTypeBehavior OutputBehavior => WorkflowItemTypeBehavior.Passthrough;

    public record Config
    {
        /// <summary>Which recipe to run. Absent means the one the lead's kind implies.</summary>
        public int? RecipeDefinitionId { get; init; }

        /// <summary>
        /// Which of the resource's leads to use, by kind. Absent takes whichever comes first —
        /// a resource usually has one.
        /// </summary>
        public AcquisitionLeadKind? PreferredLeadKind { get; init; }
    }

    public async Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item,
        CancellationToken ct)
    {
        if (item is not IHasResourceId identified)
        {
            throw new WorkflowActivityConfigException(
                $"{Kind} was given a {item.GetType().Name}, which does not name a resource.");
        }

        var config = ctx.GetConfig<Config>();
        var leads = await ctx.Services.GetRequiredService<IAcquisitionLeadService>()
            .GetByResourceId(identified.ResourceId);

        var lead = config?.PreferredLeadKind is { } preferred
            ? leads.FirstOrDefault(l => l.Kind == preferred) ?? leads.FirstOrDefault()
            : leads.FirstOrDefault();

        if (lead == null)
        {
            // Nothing says where to get it. Dropping the item is right: the chain may well be
            // running over a list where only some of them have a lead.
            ctx.Logger.LogInformation(
                "[Acquisition] Resource {ResourceId} has nowhere to be got from; skipping",
                identified.ResourceId);

            return WorkflowItemOutcome.DropItem;
        }

        try
        {
            await ctx.Services.GetRequiredService<IAcquisitionService>().CreateAsync(
                identified.ResourceId, lead.Kind, lead.Value, lead.Id == 0 ? null : lead.Id,
                config?.RecipeDefinitionId, ct: ct);
        }
        catch (InvalidOperationException ex)
        {
            // Already has files, or is already being acquired. Both are fine outcomes for a chain
            // running over a list, and neither is worth failing the run for.
            ctx.Logger.LogInformation("[Acquisition] Not acquiring resource {ResourceId}: {Reason}",
                identified.ResourceId, ex.Message);
        }

        return WorkflowItemOutcome.KeepItem;
    }
}
