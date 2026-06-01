using Bakabase.Modules.Subscription.Abstractions.Models.Domain;

namespace Bakabase.Modules.Subscription.Abstractions.Components;

/// <summary>
/// Default implementation for "fetch a list, diff by item id". Most providers (search feeds,
/// user follows, bookmarks) match this shape — they only need to override
/// <see cref="FetchCurrentItemsAsync"/>. Providers that need finer-grained "update" semantics
/// (e.g. an existing gallery gained pages) should implement <see cref="ISubscriptionProvider"/>
/// directly instead.
/// </summary>
public abstract class AbstractListIdDiffProvider : ISubscriptionProvider
{
    public abstract string Kind { get; }
    public abstract string DisplayName { get; }
    public virtual string? Icon => null;

    public abstract Task<SubscriptionValidationResult> ValidateTargetAsync(string targetJson, CancellationToken ct);
    public abstract string DescribeTarget(string targetJson);

    public virtual async Task<SubscriptionCheckResult> CheckAsync(
        SubscriptionRecord subscription,
        SubscriptionSnapshot? lastSnapshot,
        CancellationToken ct)
    {
        var current = await FetchCurrentItemsAsync(subscription, ct);

        var oldIds = lastSnapshot?.Items.Select(i => i.Id).ToHashSet() ?? [];
        var newItems = current.Where(i => !oldIds.Contains(i.Id)).ToList();

        return new SubscriptionCheckResult(
            new SubscriptionSnapshot { Items = current.ToList() },
            newItems,
            UpdatedItems: []);
    }

    protected abstract Task<IReadOnlyList<SubscriptionItem>> FetchCurrentItemsAsync(
        SubscriptionRecord subscription,
        CancellationToken ct);
}
