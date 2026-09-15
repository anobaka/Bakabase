namespace Bakabase.Modules.Subscription.Abstractions.Models.Input;

public record SubscriptionCreationInputModel
{
    public string Kind { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string TargetJson { get; set; } = null!;
    public bool Enabled { get; set; } = true;
    public int? IntervalMinutes { get; set; }

    /// <summary>
    /// Which collection this fills. Absent creates one named after the subscription — a source
    /// without a collection has nowhere to put what it finds.
    /// </summary>
    public int? CollectionId { get; set; }
}
