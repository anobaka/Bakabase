using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.Subscription.Abstractions.Models.Db;

public record SubscriptionDbModel
{
    [Key] public int Id { get; set; }

    /// <summary>Provider identifier, e.g. "exhentai.search".</summary>
    public string Kind { get; set; } = null!;

    /// <summary>User-supplied display name.</summary>
    public string DisplayName { get; set; } = null!;

    /// <summary>Opaque to the framework — each Provider parses this into its own typed shape.</summary>
    public string TargetJson { get; set; } = null!;

    public bool Enabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastCheckedAt { get; set; }
    public DateTime? LastChangeAt { get; set; }
    public string? LastError { get; set; }

    /// <summary>
    /// Optional per-subscription override of the global check interval. Phase 2 leaves this
    /// nullable + unused; per-kind / per-subscription scheduling lands later.
    /// </summary>
    public int? IntervalMinutes { get; set; }
}
