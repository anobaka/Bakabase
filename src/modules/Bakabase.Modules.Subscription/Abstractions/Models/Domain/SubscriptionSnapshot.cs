namespace Bakabase.Modules.Subscription.Abstractions.Models.Domain;

public record SubscriptionSnapshot
{
    public List<SubscriptionItem> Items { get; init; } = [];
}
