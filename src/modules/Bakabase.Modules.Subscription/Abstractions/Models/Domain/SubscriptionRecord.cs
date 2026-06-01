namespace Bakabase.Modules.Subscription.Abstractions.Models.Domain;

public record SubscriptionRecord
{
    public int Id { get; set; }
    public string Kind { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string TargetJson { get; set; } = null!;
    public bool Enabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastCheckedAt { get; set; }
    public DateTime? LastChangeAt { get; set; }
    public string? LastError { get; set; }
    public int? IntervalMinutes { get; set; }
}
