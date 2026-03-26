using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Db;

public record ResourceSourceLinkDbModel
{
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public ResourceSource Source { get; set; }
    public string SourceKey { get; set; } = null!;
    public DateTime CreateDt { get; set; }
    /// <summary>
    /// Serialized List&lt;string&gt; of external cover URLs.
    /// </summary>
    public string? CoverUrls { get; set; }
    /// <summary>
    /// Serialized List&lt;string&gt; of local file paths for downloaded covers.
    /// </summary>
    public string? LocalCoverPaths { get; set; }
    /// <summary>
    /// When set, indicates that the last cover download attempt failed for all URLs.
    /// Used to implement backoff so broken links aren't retried every task cycle.
    /// </summary>
    public DateTime? CoverDownloadFailedAt { get; set; }
}
