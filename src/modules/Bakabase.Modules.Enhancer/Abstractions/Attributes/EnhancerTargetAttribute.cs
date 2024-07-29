using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Field)]
public class EnhancerTargetAttribute(
    EnhancerTargetType type,
    StandardValueType valueType,
    EnhancerTargetOptionsItem[]? options = null,
    bool isDynamic = false,
    string? description = null) : Attribute
{
    public EnhancerTargetType Type { get; set; } = type;
    public StandardValueType ValueType { get; } = valueType;
    public EnhancerTargetOptionsItem[]? Options { get; } = options;
    public bool IsDynamic { get; } = isDynamic;
    public string? Description { get; } = description;
}