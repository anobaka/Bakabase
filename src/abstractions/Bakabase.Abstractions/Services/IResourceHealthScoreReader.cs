namespace Bakabase.Abstractions.Services;

/// <summary>
/// Read-only access to a resource's aggregated health score.
/// Implemented by the HealthScore module; consumed by the search index so it can
/// surface the score as an Internal Property without depending on the module.
/// </summary>
public interface IResourceHealthScoreReader
{
    /// <summary>
    /// Returns the aggregated final score for a resource, or null when no enabled
    /// profile has scored it. Should be O(1) — backed by an in-memory cache.
    /// </summary>
    decimal? GetAggregatedScore(int resourceId);
}
