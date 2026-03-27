using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Services;

/// <summary>
/// Provides metadata for resources from a specific external source.
/// </summary>
public interface IMetadataProvider
{
    /// <summary>
    /// Identifies this provider for readiness tracking in <see cref="ResourceDataState"/>.
    /// </summary>
    DataOrigin Origin { get; }

    /// <summary>
    /// Returns the predefined metadata fields this provider can supply.
    /// These are shown as fixed rows in the metadata mapping UI.
    /// </summary>
    List<SourceMetadataFieldInfo> GetPredefinedMetadataFields();

    /// <summary>
    /// Fetches detailed metadata for an item identified by sourceKey.
    /// </summary>
    /// <param name="sourceKey">The source-specific key (e.g., Steam AppId, DLsite WorkId).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Detailed metadata, or null if fetch failed.</returns>
    Task<SourceDetailedMetadata?> FetchMetadataAsync(string sourceKey, CancellationToken ct);
}
