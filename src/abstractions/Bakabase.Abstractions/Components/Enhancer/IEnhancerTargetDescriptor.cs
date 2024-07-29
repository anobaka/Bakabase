using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Components.Enhancer;

public interface IEnhancerTargetDescriptor
{
    int Id { get; }
    string Name { get; }
    Enum EnumId { get; }
    StandardValueType ValueType { get; }
    bool IsDynamic { get; }
    string? Description { get; }
    int[]? OptionsItems { get; }
}