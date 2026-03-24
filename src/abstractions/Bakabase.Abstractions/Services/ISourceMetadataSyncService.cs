using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Services;

public interface ISourceMetadataSyncService
{
    /// <summary>
    /// Get all metadata mappings for a source.
    /// </summary>
    Task<List<SourceMetadataMapping>> GetMappings(ResourceSource source);

    /// <summary>
    /// Save metadata mappings for a source. Removes mappings that were previously configured but are no longer included.
    /// Clears property values for removed mappings (only the source's enhancer scope).
    /// </summary>
    Task SaveMappings(ResourceSource source, List<SourceMetadataMapping> mappings);

    /// <summary>
    /// Sync metadata from a specific source to resource properties for a single resource.
    /// Uses the source's corresponding enhancer PropertyValueScope.
    /// </summary>
    Task SyncMetadataToProperties(int resourceId, ResourceSource source, CancellationToken ct);

    /// <summary>
    /// Batch sync metadata from a specific source to all linked resources.
    /// </summary>
    Task SyncMetadataToPropertiesBatch(ResourceSource source, Action<int>? onProgress, CancellationToken ct);

    /// <summary>
    /// Get the list of available metadata fields for a source.
    /// </summary>
    List<string> GetAvailableMetadataFields(ResourceSource source);
}
