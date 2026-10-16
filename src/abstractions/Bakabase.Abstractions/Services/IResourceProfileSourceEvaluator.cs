using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Services;

/// <summary>Evaluates bounded, read-only profile filters against this source library's own IDs and definitions.</summary>
public interface IResourceProfileSourceEvaluator
{
    /// <summary>Resolves a source-owned filter without silently dropping missing properties or unsupported operations.</summary>
    Task<ResourceSearch> ParseSearchAsync(string? searchJson, CancellationToken cancellationToken = default);

    Task<ResourceProfileEvaluationResult> EvaluateAsync(ResourceProfileEvaluationInput input,
        CancellationToken cancellationToken = default);
}
