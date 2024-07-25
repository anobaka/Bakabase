using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Components.Enhancer;

public interface IEnhancer
{
    int Id { get; }
    Task<List<EnhancementRawValue>?> CreateEnhancements(Resource resource, object? options);
}
