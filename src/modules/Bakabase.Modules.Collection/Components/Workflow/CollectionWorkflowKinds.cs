namespace Bakabase.Modules.Collection.Components.Workflow;

/// <summary>
/// The names collections use inside the workflow engine.
/// </summary>
public static class CollectionWorkflowKinds
{
    /// <summary>
    /// Fires when resources join a collection, however they got there. This is the join between
    /// the two halves: a subscription fills a collection, and "when something joins, go and get it"
    /// is a workflow the user builds rather than a feature anyone had to write.
    /// </summary>
    public const string TriggerMembersAdded = "collection.membersAdded";

    /// <summary>What <see cref="TriggerMembersAdded"/> emits: one item per resource that joined.</summary>
    public const string ItemMember = "item.collection.member";

    /// <summary>The activity picker's bucket.</summary>
    public const string ActivityGroup = "collection";
}
