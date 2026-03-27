using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Services;

/// <summary>
/// Orchestrates playable item resolution across all <see cref="IPlayableItemProvider"/>s.
/// Aggregates results from all applicable providers.
/// </summary>
public interface IPlayableItemProviderService
{
    /// <summary>
    /// Gets all playable items for a resource by calling all applicable providers and merging results.
    /// </summary>
    Task<AggregatedPlayableItems> GetPlayableItemsAsync(Resource resource, CancellationToken ct);

    /// <summary>
    /// Plays a specific item by dispatching to the appropriate provider based on origin.
    /// </summary>
    Task PlayAsync(Resource resource, DataOrigin origin, string key, CancellationToken ct);

    /// <summary>
    /// Invalidates all provider caches for a resource's playable items.
    /// </summary>
    Task InvalidateAllAsync(int resourceId);
}

public record AggregatedPlayableItems(List<PlayableItem> Items);
