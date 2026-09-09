using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.Subscription.Abstractions.Models.Db;

/// <summary>
/// Retired. A subscription's difference is now measured against its collection's members, which
/// are real resources rather than a rolling snapshot of strings.
/// <para>
/// The table stays for one release so a downgrade still finds it. Nothing reads or writes it; the
/// next version drops it.
/// </para>
/// </summary>
[Obsolete("Difference is measured against collection members. Kept for one release; do not read or write.")]
public record SubscriptionSnapshotDbModel
{
    [Key] public int SubscriptionId { get; set; }

    public string SnapshotJson { get; set; } = null!;

    public DateTime UpdatedAt { get; set; }
}
