using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public class ResourceSourceLink
{
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public ResourceSource Source { get; set; }
    public string SourceKey { get; set; } = null!;
    public DateTime CreateDt { get; set; }

    /// <summary>
    /// Cover URLs from the external source (e.g., DLsite cover URLs, Steam header image URL).
    /// Stored as serialized ListString.
    /// </summary>
    public List<string>? CoverUrls { get; set; }

    /// <summary>
    /// Local file paths where external covers have been downloaded.
    /// When CoverUrls is non-empty but LocalCoverPaths is empty, the DownloadExternalCovers task will download them.
    /// </summary>
    public List<string>? LocalCoverPaths { get; set; }
}
