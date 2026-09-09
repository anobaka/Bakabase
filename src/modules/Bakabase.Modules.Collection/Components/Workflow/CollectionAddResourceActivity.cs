using Bakabase.Modules.Collection.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Collection.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Collection.Components.Workflow;

/// <summary>
/// Puts a resource in a collection from inside a workflow.
/// <para>
/// The other direction of <see cref="CollectionMembersAddedTrigger"/>: a chain that finds things
/// — a subscription's new listings, a folder scan, a search — can file them under a name, and
/// everything that watches that collection then happens on its own.
/// </para>
/// </summary>
public class CollectionAddResourceActivity : IWorkflowActivity
{
    public string Kind => "action.collection.addResource";
    public string DisplayName => "Add to a collection";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Action;
    public string Group => CollectionWorkflowKinds.ActivityGroup;

    /// <summary>Anything that names a resource — the item need not have come from a collection.</summary>
    public Type? AcceptedItemInterface => typeof(IHasResourceId);

    public WorkflowItemTypeBehavior OutputBehavior => WorkflowItemTypeBehavior.Passthrough;

    public record Config
    {
        /// <summary>Which collection to add to.</summary>
        public int? CollectionId { get; init; }
    }

    public async Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item,
        CancellationToken ct)
    {
        if (item is not IHasResourceId identified)
        {
            throw new WorkflowActivityConfigException(
                $"{Kind} was given a {item.GetType().Name}, which does not name a resource.");
        }

        if (ctx.GetConfig<Config>()?.CollectionId is not { } collectionId)
        {
            throw new WorkflowActivityConfigException($"{Kind} needs a collection to add to.");
        }

        var collections = ctx.Services.GetRequiredService<ICollectionService>();

        if (await collections.Get(collectionId, false, ct) == null)
        {
            // The collection was deleted after the workflow was written. Failing the run says so
            // once, where a silent skip would leave a chain quietly doing nothing forever.
            throw new WorkflowActivityConfigException($"Collection #{collectionId} no longer exists.");
        }

        // Adding twice is a no-op that announces nothing, so a chain that re-runs over the same
        // list does not keep waking everything that watches the collection.
        await collections.AddMembers(collectionId, [identified.ResourceId],
            CollectionMembershipOrigin.Manual, null, ct);

        ctx.Logger.LogInformation("[Collection] Resource {ResourceId} is now in collection {CollectionId}",
            identified.ResourceId, collectionId);

        return WorkflowItemOutcome.KeepItem;
    }
}
