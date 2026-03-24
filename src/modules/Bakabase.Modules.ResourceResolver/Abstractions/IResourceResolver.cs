using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.ResourceResolver.Abstractions;

/// <summary>
/// Discovers and manages resources from a specific external source.
/// Metadata retrieval is handled by corresponding Enhancers.
/// </summary>
public interface IResourceResolver
{
    /// <summary>
    /// The source this resolver handles.
    /// </summary>
    ResourceSource Source { get; }

    /// <summary>
    /// Discovers resources from the external source.
    /// Results are converted to PathMark effects and participate in the sync pipeline.
    /// </summary>
    Task<List<ResolvedResource>> DiscoverResources(CancellationToken ct);

    /// <summary>
    /// Gets the configuration schema for this resolver (used by frontend to render settings UI).
    /// </summary>
    ResolverConfigurationSchema GetConfigurationSchema();

    /// <summary>
    /// Gets the default player configuration for this source.
    /// </summary>
    ResolverPlayerConfig? GetDefaultPlayerConfig();

    /// <summary>
    /// Gets the playable file selector for this source, if applicable.
    /// </summary>
    IPlayableFileSelector? GetPlayableFileSelector();

    /// <summary>
    /// Identifies FileSystem resources that can be migrated to this source.
    /// </summary>
    Task<List<MigrationCandidate>> IdentifyMigrationCandidates(
        List<Resource> fileSystemResources, CancellationToken ct);

    /// <summary>
    /// Executes migration for the given candidates.
    /// </summary>
    Task MigrateResources(List<MigrationCandidate> candidates, CancellationToken ct);

    /// <summary>
    /// Gets default display names for the given source keys.
    /// Used to populate DisplayName when no name template is configured.
    /// </summary>
    /// <param name="sourceKeys">Source keys to look up.</param>
    /// <returns>A dictionary mapping source key to display name. Keys not found are omitted.</returns>
    Task<Dictionary<string, string>> GetDefaultDisplayNames(IEnumerable<string> sourceKeys);

    /// <summary>
    /// Discovers playable items for a resource from this source.
    /// Returns items that can be played (files, URIs, etc.).
    /// </summary>
    /// <param name="resource">The resource to discover playable items for.</param>
    /// <param name="sourceKey">The source key identifying the resource in this source.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of playable items, or empty if none found.</returns>
    Task<List<PlayableItem>> DiscoverPlayableItemsAsync(Resource resource, string sourceKey, CancellationToken ct);

    /// <summary>
    /// Plays a specific playable item from this source.
    /// Each resolver knows how to launch its own items (file, URI, etc.).
    /// </summary>
    /// <param name="resource">The resource being played.</param>
    /// <param name="item">The playable item to play.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PlayAsync(Resource resource, PlayableItem item, CancellationToken ct);
}
