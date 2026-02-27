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
    /// Gets the cover image for a specific resource.
    /// </summary>
    Task<ResolvedCover?> GetCover(Resource resource, CancellationToken ct);

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
}
