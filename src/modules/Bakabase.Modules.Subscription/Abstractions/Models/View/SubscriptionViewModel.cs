using Bakabase.Modules.Subscription.Abstractions.Models.Domain;

namespace Bakabase.Modules.Subscription.Abstractions.Models.View;

public record SubscriptionViewModel
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

    /// <summary>Provider-rendered short summary of <see cref="TargetJson"/>; provided by the server so the UI doesn't reparse.</summary>
    public string? TargetSummary { get; set; }

    public static SubscriptionViewModel From(SubscriptionRecord s, string? targetSummary = null) => new()
    {
        Id = s.Id,
        Kind = s.Kind,
        DisplayName = s.DisplayName,
        TargetJson = s.TargetJson,
        Enabled = s.Enabled,
        CreatedAt = s.CreatedAt,
        LastCheckedAt = s.LastCheckedAt,
        LastChangeAt = s.LastChangeAt,
        LastError = s.LastError,
        IntervalMinutes = s.IntervalMinutes,
        TargetSummary = targetSummary,
    };
}
