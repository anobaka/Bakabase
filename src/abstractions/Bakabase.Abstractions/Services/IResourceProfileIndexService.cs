namespace Bakabase.Abstractions.Services;

/// <summary>
/// Maintains an in-memory index for Resource-ResourceProfile matching.
/// This avoids expensive real-time filter evaluation for every query.
/// </summary>
public interface IResourceProfileIndexService
{
    /// <summary>
    /// Get profile IDs matching a resource, sorted by profile priority (highest first)
    /// </summary>
    Task<IReadOnlyList<int>> GetMatchingProfileIds(int resourceId);

    /// <summary>
    /// Get profile IDs matching multiple resources (batch operation to avoid N+1)
    /// Returns a dictionary mapping resourceId to profileIds (sorted by priority, highest first)
    /// </summary>
    Task<Dictionary<int, IReadOnlyList<int>>> GetMatchingProfileIdsForResources(IEnumerable<int> resourceIds);

    /// <summary>
    /// Get resource IDs matching a profile
    /// </summary>
    Task<IReadOnlySet<int>> GetMatchingResourceIds(int profileId);

    /// <summary>
    /// Check if index is ready (initial build completed)
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Wait until the index is ready
    /// </summary>
    Task WaitUntilReady(CancellationToken ct = default);

    /// <summary>
    /// Invalidate cache for a specific resource.
    /// The resource will be re-evaluated against all profiles.
    /// </summary>
    void InvalidateResource(int resourceId);

    /// <summary>
    /// Invalidate cache for multiple resources.
    /// </summary>
    void InvalidateResources(IEnumerable<int> resourceIds);

    /// <summary>
    /// Invalidate cache for a specific profile.
    /// All resources will be re-evaluated against this profile.
    /// </summary>
    void InvalidateProfile(int profileId);

    /// <summary>
    /// Invalidate cache for all profiles (e.g., when profile priorities change).
    /// Triggers a full rebuild.
    /// </summary>
    void InvalidateAllProfiles();

    /// <summary>
    /// Trigger a full index rebuild
    /// </summary>
    void TriggerFullRebuild();

    /// <summary>
    /// Perform a full index rebuild with progress reporting
    /// </summary>
    Task RebuildAsync(Func<int, string?, Task>? onProgress, CancellationToken ct);
}
