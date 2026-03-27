using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Services;

/// <summary>
/// Provides playable items for resources from a specific origin.
/// Results from all applicable providers are aggregated (merged).
/// Each provider manages its own internal caching.
/// </summary>
public interface IPlayableItemProvider
{
    /// <summary>
    /// Identifies this provider for readiness tracking in <see cref="ResourceDataState"/>.
    /// </summary>
    DataOrigin Origin { get; }

    /// <summary>
    /// Priority for display ordering. Lower number = shown first in the UI.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Whether this provider can potentially supply playable items for the given resource.
    /// </summary>
    bool AppliesTo(Resource resource);

    /// <summary>
    /// Gets playable items for a resource from this origin.
    /// Returns cached data if available; otherwise discovers and caches internally.
    /// </summary>
    Task<PlayableItemProviderResult> GetPlayableItemsAsync(Resource resource, CancellationToken ct);

    /// <summary>
    /// Launches/plays a specific item from this origin.
    /// </summary>
    Task PlayAsync(Resource resource, PlayableItem item, CancellationToken ct);

    /// <summary>
    /// Gets the current readiness status of this provider for the given resource.
    /// Derived from internal state (no DB query needed for most providers).
    /// </summary>
    DataStatus GetStatus(Resource resource);

    /// <summary>
    /// Invalidates cached playable item data for a resource.
    /// </summary>
    Task InvalidateAsync(int resourceId);
}

public record PlayableItemProviderResult(List<PlayableItem> Items);
