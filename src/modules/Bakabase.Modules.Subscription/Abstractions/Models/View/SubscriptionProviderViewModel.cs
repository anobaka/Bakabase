namespace Bakabase.Modules.Subscription.Abstractions.Models.View;

public record SubscriptionProviderViewModel
{
    public string Kind { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Icon { get; set; }
}
