using Bakabase.Abstractions.Components.Events;

namespace Bakabase.Service.Components.Collections;

/// <summary>
/// Drops the cached answers to collection rules whenever a resource changes.
/// <para>
/// A rule's answer is a full search, and the collections list asks every collection for its
/// progress at once — so the answer is cached. It can only become wrong because a resource changed,
/// which makes this the whole of the invalidation policy.
/// </para>
/// </summary>
public class CollectionRuleCacheInvalidator
{
    public CollectionRuleCacheInvalidator(IResourceDataChangeEvent events, CollectionRuleCache cache)
    {
        events.OnResourceDataChanged += _ => cache.Invalidate();
        events.OnResourceRemoved += _ => cache.Invalidate();
    }
}
