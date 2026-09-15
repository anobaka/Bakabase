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

    /// <summary>How often to check this one. Null uses the global default.</summary>
    public int? IntervalMinutes { get; set; }

    /// <summary>
    /// The collection this source fills. Nullable only for the length of the upgrade that adds
    /// it — a runtime migrator gives every existing subscription a collection of its own name.
    /// </summary>
    public int? CollectionId { get; set; }
}
