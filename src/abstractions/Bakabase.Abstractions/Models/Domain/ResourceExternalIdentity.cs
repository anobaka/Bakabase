using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// Identifies a work on an external site. This association does not imply that the site
/// supplied the resource's files or that the user owns a copy there.
/// </summary>
public class ResourceExternalIdentity
{
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public ThirdPartyId ThirdPartyId { get; set; }
    public string ExternalId { get; set; } = null!;
    public DateTime CreateDt { get; set; }

    /// <summary>Cover URLs already known from parsing or metadata enhancement.</summary>
    public List<string>? CoverUrls { get; set; }
    public List<string>? LocalCoverPaths { get; set; }
    public DateTime? CoverDownloadFailedAt { get; set; }
    public string? MetadataJson { get; set; }
}
