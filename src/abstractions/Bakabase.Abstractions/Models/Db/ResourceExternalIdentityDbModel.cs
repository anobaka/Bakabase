using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Abstractions.Models.Db;

public record ResourceExternalIdentityDbModel
{
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public ThirdPartyId ThirdPartyId { get; set; }
    public string ExternalId { get; set; } = null!;
    public DateTime CreateDt { get; set; }
    public string? CoverUrls { get; set; }
    public string? LocalCoverPaths { get; set; }
    public DateTime? CoverDownloadFailedAt { get; set; }
    public string? MetadataJson { get; set; }
}
