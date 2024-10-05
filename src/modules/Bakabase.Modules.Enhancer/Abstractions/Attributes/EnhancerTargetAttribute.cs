using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Field)]
public class EnhancerTargetAttribute(
    StandardValueType valueType,
    PropertyType propertyType,
    EnhancerTargetOptionsItem[]? options = null,
    bool isDynamic = false,
    Type? converter = null,
    ReservedProperty reservedPropertyCandidate = default,
    string? description = null) : Attribute
{
    public StandardValueType ValueType { get; } = valueType;
    public PropertyType PropertyType { get; } = propertyType;
    public EnhancerTargetOptionsItem[]? Options { get; } = options;
    public bool IsDynamic { get; } = isDynamic;
    public string? Description { get; } = description;
    public Type? Converter { get; } = converter;
    public ReservedProperty ReservedPropertyCandidate { get; } = reservedPropertyCandidate;
}