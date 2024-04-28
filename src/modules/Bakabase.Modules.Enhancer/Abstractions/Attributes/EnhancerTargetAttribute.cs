using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Abstractions.Attributes;

public class EnhancerTargetAttribute(StandardValueType valueType, string? name = null, string? description = null)
    : Attribute
{
    public string? Name { get; set; } = name;
    public StandardValueType ValueType { get; } = valueType;
    public string? Description { get; set; } = description;
}