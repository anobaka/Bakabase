using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Services;

/// <summary>
/// Orchestrates cover resolution across all <see cref="ICoverProvider"/>s.
/// Iterates providers by priority and returns the first non-null result.
/// </summary>
public interface ICoverProviderService
{
    /// <summary>
    /// Resolves covers for a resource by iterating all applicable providers by priority.
    /// Returns the first non-null result.
    /// </summary>
    Task<List<string>?> ResolveCoversAsync(Resource resource, CancellationToken ct);

    /// <summary>
    /// Invalidates all provider caches for a resource's covers.
    /// </summary>
    Task InvalidateAllAsync(int resourceId);
}
