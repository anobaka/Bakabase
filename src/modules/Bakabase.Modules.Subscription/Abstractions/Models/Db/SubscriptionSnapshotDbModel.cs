using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.Subscription.Abstractions.Models.Db;

/// <summary>
/// Only the most recent snapshot per subscription — overwritten on every check, so the
/// table never accumulates history.
/// </summary>
public record SubscriptionSnapshotDbModel
{
    [Key] public int SubscriptionId { get; set; }

    /// <summary>Serialized <c>SubscriptionSnapshot</c>.</summary>
    public string SnapshotJson { get; set; } = null!;

    public DateTime UpdatedAt { get; set; }
}
