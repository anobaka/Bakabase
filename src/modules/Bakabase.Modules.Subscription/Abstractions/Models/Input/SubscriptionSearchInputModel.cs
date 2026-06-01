namespace Bakabase.Modules.Subscription.Abstractions.Models.Input;

public record SubscriptionSearchInputModel
{
    public string? Kind { get; set; }
    public bool? EnabledOnly { get; set; }
}
