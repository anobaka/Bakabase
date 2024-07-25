using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Field)]
public class EnhancerTargetAttribute(
    StandardValueType valueType,
    EnhancerTargetOptionsItem[]? options = null,
    bool isDynamic = false,
    string? description = null) : Attribute
{
    public StandardValueType ValueType { get; } = valueType;
    public EnhancerTargetOptionsItem[]? Options { get; } = options;
    public bool IsDynamic { get; } = isDynamic;
    public string? Description { get; } = description;
}