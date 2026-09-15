using Bakabase.Modules.Collection.Abstractions.Models.Db;
using Bakabase.Modules.Collection.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Collection.Abstractions.Models.Domain;

/// <summary>How much of a collection is actually here.</summary>
/// <param name="Total">Members that count — ignored ones are not among them.</param>
/// <param name="Owned">Members with local files.</param>
/// <param name="Acquiring">Members something is currently getting.</param>
/// <param name="Ignored">Members explicitly set aside; outside the ratio entirely.</param>
public record CollectionProgress(int Total, int Owned, int Acquiring, int Ignored)
{
    /// <summary>
    /// 0 to 1. A collection with nothing in it counts as complete rather than as zero: an empty
    /// collection is not 0% collected, it is not a question yet.
    /// </summary>
    public double Ratio => Total == 0 ? 1 : (double) Owned / Total;
}

/// <summary>
/// Named <c>ResourceCollection</c> rather than <c>Collection</c>: the module's own namespace ends in
/// <c>Collection</c>, and a type of the same name shadows it everywhere inside.
/// </summary>
public record ResourceCollection
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? Color { get; set; }
    public string? CoverPath { get; set; }
    public string? RuleSearchJson { get; set; }
    public bool AutoAcquire { get; set; }
    public string? AcquisitionSettingsJson { get; set; }
    public int Order { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Filled in for the list and the card; not stored.</summary>
    public CollectionProgress? Progress { get; set; }

    public bool HasRule => !string.IsNullOrWhiteSpace(RuleSearchJson);
}

/// <summary>One membership, as the rest of the app sees it.</summary>
public record CollectionMember
{
    public int CollectionId { get; set; }
    public int ResourceId { get; set; }

    /// <summary>Null for a member that is here because it matches the rule.</summary>
    public CollectionMembershipOrigin? Origin { get; set; }

    public int? SubscriptionId { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public bool IsIgnored { get; set; }
    public int? Order { get; set; }

    /// <summary>True when nothing was written down — the rule is what puts it here.</summary>
    public bool IsFromRule => Origin == null;
}

public static class CollectionExtensions
{
    public static ResourceCollection ToDomainModel(this CollectionDbModel db) => new()
    {
        Id = db.Id,
        Name = db.Name,
        Description = db.Description,
        Color = db.Color,
        CoverPath = db.CoverPath,
        RuleSearchJson = db.RuleSearchJson,
        AutoAcquire = db.AutoAcquire,
        AcquisitionSettingsJson = db.AcquisitionSettingsJson,
        Order = db.Order,
        CreatedAt = db.CreatedAt,
        UpdatedAt = db.UpdatedAt,
    };

    public static CollectionMember ToDomainModel(this CollectionResourceMappingDbModel db) => new()
    {
        CollectionId = db.CollectionId,
        ResourceId = db.ResourceId,
        Origin = db.Origin,
        SubscriptionId = db.SubscriptionId,
        LastSeenAt = db.LastSeenAt,
        IsIgnored = db.IsIgnored,
        Order = db.Order,
    };
}
