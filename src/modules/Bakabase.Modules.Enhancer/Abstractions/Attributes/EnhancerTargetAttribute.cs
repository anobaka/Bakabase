using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Field)]
public class EnhancerTargetAttribute(
    StandardValueType valueType,
    CustomPropertyType customPropertyType,
    EnhancerTargetOptionsItem[]? options = null,
    bool isDynamic = false,
    Type? converter = null,
    string? description = null) : Attribute
{
    public StandardValueType ValueType { get; } = valueType;
    public CustomPropertyType CustomPropertyType { get; } = customPropertyType;
    public EnhancerTargetOptionsItem[]? Options { get; } = options;
    public bool IsDynamic { get; } = isDynamic;
    public string? Description { get; } = description;
    public Type? Converter { get; } = converter;
}