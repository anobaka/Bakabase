using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;

namespace Bakabase.Modules.Enhancer.Abstractions.Components;

public interface IEnhancer
{
    int Id { get; }
    Task<List<EnhancementRawValue>?> CreateEnhancements(Resource resource, EnhancerFullOptions options, CancellationToken ct);
}
