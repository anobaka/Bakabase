using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Services;

/// <summary>
/// Provides cover data for resources from a specific origin.
/// Covers are priority-based: the first provider (by priority) that returns data wins.
/// Each provider manages its own internal caching.
/// </summary>
public interface ICoverProvider
{
    /// <summary>
    /// Identifies this provider for readiness tracking in <see cref="ResourceDataState"/>.
    /// </summary>
    DataOrigin Origin { get; }

    /// <summary>
    /// Priority for cover resolution. Lower number = higher priority.
    /// When multiple providers can supply covers, the highest-priority one wins.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Whether this provider can potentially supply covers for the given resource.
    /// </summary>
    bool AppliesTo(Resource resource);

    /// <summary>
    /// Gets cover paths for a resource.
    /// Returns cached data if available; otherwise discovers/downloads and caches internally.
    /// </summary>
    /// <returns>Local cover file paths, or null if this provider has no covers for this resource.</returns>
    Task<List<string>?> GetCoversAsync(Resource resource, CancellationToken ct);

    /// <summary>
    /// Gets the current readiness status of this provider for the given resource.
    /// Derived from internal state (no DB query needed for most providers).
    /// </summary>
    DataStatus GetStatus(Resource resource);

    /// <summary>
    /// Invalidates cached cover data for a resource, forcing re-discovery on next call.
    /// </summary>
    Task InvalidateAsync(int resourceId);
}
