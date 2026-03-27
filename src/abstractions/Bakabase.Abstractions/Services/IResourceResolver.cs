using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Services;

/// <summary>
/// Discovers and manages resources from a specific source.
/// Only handles resource discovery and migration.
/// Cover/PlayableItem/Metadata are handled by their respective Provider interfaces.
/// </summary>
public interface IResourceResolver
{
    /// <summary>
    /// The source this resolver handles.
    /// </summary>
    ResourceSource Source { get; }

    /// <summary>
    /// Discovers resources from the source.
    /// Results are converted to PathMark effects and participate in the sync pipeline.
    /// </summary>
    Task<List<ResolvedResource>> DiscoverResources(CancellationToken ct);

    /// <summary>
    /// Gets the configuration schema for this resolver (used by frontend to render settings UI).
    /// </summary>
    ResolverConfigurationSchema GetConfigurationSchema();

    /// <summary>
    /// Identifies PathMark resources that can be migrated to this source.
    /// </summary>
    Task<List<MigrationCandidate>> IdentifyMigrationCandidates(
        List<Resource> pathMarkResources, CancellationToken ct);

    /// <summary>
    /// Executes migration for the given candidates.
    /// </summary>
    Task MigrateResources(List<MigrationCandidate> candidates, CancellationToken ct);

    /// <summary>
    /// Gets default display names for the given source keys.
    /// Used to populate DisplayName when no name template is configured.
    /// </summary>
    Task<Dictionary<string, string>> GetDefaultDisplayNames(IEnumerable<string> sourceKeys);
}
