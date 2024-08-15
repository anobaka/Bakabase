using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Abstractions;

public interface IEnhancerTargetDescriptor
{
    int Id { get; }
    string Name { get; }
    Enum EnumId { get; }
    StandardValueType ValueType { get; }
    CustomPropertyType CustomPropertyType { get; }
    bool IsDynamic { get; }
    string? Description { get; }
    int[]? OptionsItems { get; }
}