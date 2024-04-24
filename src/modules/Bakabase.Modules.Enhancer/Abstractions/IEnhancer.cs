using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Components.Enhancer;

public interface IEnhancer
{
    EnhancerId Id { get; }
    Task<List<EnhancementRawValue>?> CreateEnhancements(Bakabase.Abstractions.Models.Domain.Resource resource);
}
