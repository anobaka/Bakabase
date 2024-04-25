using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Abstractions;

public interface IEnhancer
{
    EnhancerId Id { get; }
    Task<List<EnhancementRawValue>?> CreateEnhancements(Bakabase.Abstractions.Models.Domain.Resource resource);
}
