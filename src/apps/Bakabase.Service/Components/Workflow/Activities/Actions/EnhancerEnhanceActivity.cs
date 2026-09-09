using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bootstrap.Components.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Workflow.Activities.Actions;

/// <summary>
/// Runs the enhancers over a resource.
/// <para>
/// The step that turns something a source named into something with a cover, a description and
/// tags. It is the natural end of most chains: a work arrives with a title and nothing else, and
/// this is what makes it look like the rest of the library.
/// </para>
/// </summary>
public class EnhancerEnhanceActivity : IWorkflowActivity
{
    public string Kind => "action.enhancer.enhance";
    public string DisplayName => "Enhance";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Action;
    public string Group => WorkflowActivityGroups.Resource;
    public Type? AcceptedItemInterface => typeof(IHasResourceId);
    public WorkflowItemTypeBehavior OutputBehavior => WorkflowItemTypeBehavior.Passthrough;

    public record Config
    {
        /// <summary>
        /// Which enhancers to run. Empty runs whatever the resource's profile says, which is
        /// almost always what is wanted — the profile is where that decision already lives.
        /// </summary>
        public int[]? EnhancerIds { get; init; }
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
        var enhancerIds = config?.EnhancerIds is {Length: > 0} ids ? ids.ToHashSet() : null;

        await using var scope = ctx.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();

        try
        {
            await scope.ServiceProvider.GetRequiredService<IEnhancerService>()
                .EnhanceResource(identified.ResourceId, enhancerIds, new PauseToken(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A site being down is not a reason to fail a run that has already done its real
            // work. Enhancement is retried on its own schedule anyway.
            ctx.Logger.LogWarning(ex, "[Workflow] Could not enhance resource {ResourceId}",
                identified.ResourceId);
        }

        return WorkflowItemOutcome.KeepItem;
    }
}
