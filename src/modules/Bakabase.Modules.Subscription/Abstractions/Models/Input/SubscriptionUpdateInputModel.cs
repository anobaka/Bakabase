namespace Bakabase.Modules.Subscription.Abstractions.Models.Input;

public record SubscriptionUpdateInputModel
{
    public string? DisplayName { get; set; }
    public string? TargetJson { get; set; }
    public bool? Enabled { get; set; }
    public int? IntervalMinutes { get; set; }
}
