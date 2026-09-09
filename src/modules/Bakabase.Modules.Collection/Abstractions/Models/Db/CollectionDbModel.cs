using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.Collection.Abstractions.Models.Db;

/// <summary>
/// A named group of resources — a series, a circle's output, a want-list, whatever the user means
/// by "these belong together".
/// <para>
/// The important thing about a collection is that its members may not be here. Missing members are
/// what makes "80% collected" a number worth showing, and they are ordinary resources without local
/// files rather than a second kind of thing.
/// </para>
/// </summary>
public record CollectionDbModel
{
    [Key] public int Id { get; set; }

    [MaxLength(128)] public string Name { get; set; } = null!;

    public string? Description { get; set; }

    /// <summary>For the card. A hex string; the interface decides what it means.</summary>
    [MaxLength(32)] public string? Color { get; set; }

    /// <summary>Relative to AppData, so moving the data folder does not break it.</summary>
    [MaxLength(1024)] public string? CoverPath { get; set; }

    /// <summary>
    /// A serialized resource search. Everything it matches is a member, without a row of its own —
    /// rule membership follows the resources and would be stale the moment it was written down.
    /// Null means the collection has no rule.
    /// </summary>
    public string? RuleSearchJson { get; set; }

    /// <summary>Start getting a member as soon as it appears with somewhere to get it from.</summary>
    public bool AutoAcquire { get; set; }

    /// <summary>Overrides some of the global acquisition settings for this collection's members.</summary>
    public string? AcquisitionSettingsJson { get; set; }

    public int Order { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// One written-down membership. Rule matches are deliberately absent: they have no state of their
/// own and would go stale, while these carry the things only a person or a source can say — that
/// this one is ignored, that it came from a subscription, where it sits in the order.
/// </summary>
public record CollectionResourceMappingDbModel
{
    [Key] public int Id { get; set; }

    public int CollectionId { get; set; }

    public int ResourceId { get; set; }

    public Domain.Constants.CollectionMembershipOrigin Origin { get; set; }

    /// <summary>Which subscription brought it in, when one did.</summary>
    public int? SubscriptionId { get; set; }

    /// <summary>When the source last listed it, so a member that disappeared can be shown as such.</summary>
    public DateTime? LastSeenAt { get; set; }

    /// <summary>
    /// Not wanted, but not forgotten either. An ignored member is out of the collected-ratio
    /// entirely — it is neither had nor missing — which is what keeps a rate honest when a series
    /// includes three drama CDs nobody wants.
    /// </summary>
    public bool IsIgnored { get; set; }

    public int? Order { get; set; }

    public DateTime CreatedAt { get; set; }
}
