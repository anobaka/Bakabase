using Bakabase.Abstractions.Components.Events;

namespace Bakabase.Service.Components.Collections;

/// <summary>
/// Marks every collection rule's answer stale whenever a resource changes.
/// <para>
/// A rule's answer is a full search, and the collections list asks every collection for its progress
/// at once — so the answer is kept. It can only become wrong because a resource changed, which makes
/// this the whole of the invalidation policy.
/// </para>
/// </summary>
public class CollectionRuleIndexInvalidator
{
    public CollectionRuleIndexInvalidator(IResourceDataChangeEvent events, CollectionRuleIndex index)
    {
        events.OnResourceDataChanged += _ => index.Invalidate();
        events.OnResourceRemoved += _ => index.Invalidate();
    }
}
