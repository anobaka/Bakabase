using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Components;

namespace Bakabase.Modules.Enhancer.Abstractions.Components;

public interface IEnhancer
{
    int Id { get; }

    /// <summary>
    /// Creates enhancement values for a resource.
    /// </summary>
    /// <param name="resource">The resource to enhance.</param>
    /// <param name="options">Enhancer options.</param>
    /// <param name="logCollector">Collector for enhancement logs.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Enhancement result containing values and logs.</returns>
    Task<EnhancementResult?> CreateEnhancements(Resource resource, EnhancerFullOptions options,
        EnhancementLogCollector logCollector, CancellationToken ct);
}
