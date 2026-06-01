namespace Bakabase.Modules.Subscription.Abstractions.Models.Domain;

public record SubscriptionCheckResult(
    SubscriptionSnapshot NewSnapshot,
    IReadOnlyList<SubscriptionItem> NewItems,
    IReadOnlyList<SubscriptionItem> UpdatedItems);
