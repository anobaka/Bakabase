using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Workflow.Activities.Actions;

/// <summary>
/// Sets one property on a resource.
/// <para>
/// The workhorse of "and then mark it": everything a source brings in can be tagged with where it
/// came from, what it is, or that it needs looking at, without a feature being written for each.
/// </para>
/// </summary>
public class ResourceSetPropertyValueActivity : IWorkflowActivity
{
    public string Kind => "action.resource.setPropertyValue";
    public string DisplayName => "Set a property";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Action;
    public string Group => WorkflowActivityGroups.Resource;
    public Type? AcceptedItemInterface => typeof(IHasResourceId);
    public WorkflowItemTypeBehavior OutputBehavior => WorkflowItemTypeBehavior.Passthrough;

    public record Config
    {
        public int? PropertyId { get; init; }

        /// <summary>Custom or reserved. Internal properties are not values anybody sets.</summary>
        public PropertyPool Pool { get; init; } = PropertyPool.Custom;

        /// <summary>The value, already serialized the way the property stores it.</summary>
        public string? Value { get; init; }

        /// <summary>
        /// Whether <see cref="Value"/> is what a person would type rather than what the database
        /// holds — a choice's label instead of its id. True lets a workflow name a tag that does
        /// not exist yet and have it created.
        /// </summary>
        public bool IsBizValue { get; init; }
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

        if (config?.PropertyId is not { } propertyId)
        {
            throw new WorkflowActivityConfigException($"{Kind} needs a property to set.");
        }

        // A child scope: the run-wide one holds the runner's tracked rows, and writing through it
        // would flush them mid-run.
        await using var scope = ctx.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IResourceService>().BulkPutPropertyValue(
            [identified.ResourceId],
            new ResourcePropertyValuePutInputModel
            {
                PropertyId = propertyId,
                IsCustomProperty = config.Pool == PropertyPool.Custom,
                Value = config.Value,
                IsBizValue = config.IsBizValue,
            });

        return WorkflowItemOutcome.KeepItem;
    }
}
