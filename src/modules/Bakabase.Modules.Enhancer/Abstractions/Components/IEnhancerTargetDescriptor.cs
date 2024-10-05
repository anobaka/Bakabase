using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Abstractions.Components;

public interface IEnhancerTargetDescriptor
{
    int Id { get; }
    string Name { get; }
    Enum EnumId { get; }
    StandardValueType ValueType { get; }
    PropertyType PropertyType { get; }
    bool IsDynamic { get; }
    string? Description { get; }
    int[]? OptionsItems { get; }
    IEnhancementConverter? EnhancementConverter { get; }
    ReservedProperty? ReservedPropertyCandidate { get; }
}