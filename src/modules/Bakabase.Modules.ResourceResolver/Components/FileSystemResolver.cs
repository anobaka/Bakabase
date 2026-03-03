using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.ResourceResolver.Abstractions;

namespace Bakabase.Modules.ResourceResolver.Components;

/// <summary>
/// FileSystem resolver adapter.
/// The actual filesystem resource discovery is handled by PathMark's existing logic
/// in PathMarkSyncService.CollectResourceEffects(). This resolver serves as a
/// placeholder that delegates discovery to the existing PathMark mechanism.
///
/// It does NOT perform its own discovery - the PathMark system handles filesystem
/// resources natively. Non-filesystem resolvers are the ones that inject resources
/// into the PathMark pipeline through the resolver integration in PathMarkSyncService.
/// </summary>
public class FileSystemResolver : IResourceResolver
{
    public ResourceSource Source => ResourceSource.FileSystem;

    /// <summary>
    /// FileSystem discovery is handled by PathMark natively.
    /// This method returns empty since PathMarkSyncService.CollectResourceEffects
    /// handles filesystem resource discovery directly.
    /// </summary>
    public Task<List<ResolvedResource>> DiscoverResources(CancellationToken ct)
    {
        // Filesystem resources are discovered by PathMark marks directly,
        // not through the resolver pipeline.
        return Task.FromResult(new List<ResolvedResource>());
    }

    /// <summary>
    /// Cover discovery for filesystem resources is handled by the existing
    /// ICoverDiscoverer service, which is called from ResourceService.
    /// </summary>
    public Task<ResolvedCover?> GetCover(Resource resource, CancellationToken ct)
    {
        // Filesystem covers are handled by ICoverDiscoverer
        return Task.FromResult<ResolvedCover?>(null);
    }

    public ResolverConfigurationSchema GetConfigurationSchema()
    {
        // No additional configuration needed - PathMarks handle filesystem configuration
        return new ResolverConfigurationSchema();
    }

    public ResolverPlayerConfig? GetDefaultPlayerConfig()
    {
        // System player (default behavior)
        return null;
    }

    public IPlayableFileSelector? GetPlayableFileSelector()
    {
        // Uses ResourceProfile-configured selector
        return null;
    }

    public Task<List<MigrationCandidate>> IdentifyMigrationCandidates(
        List<Resource> fileSystemResources, CancellationToken ct)
    {
        // Not applicable - can't migrate from FileSystem to FileSystem
        return Task.FromResult(new List<MigrationCandidate>());
    }

    public Task MigrateResources(List<MigrationCandidate> candidates, CancellationToken ct)
    {
        // Not applicable
        return Task.CompletedTask;
    }
}
