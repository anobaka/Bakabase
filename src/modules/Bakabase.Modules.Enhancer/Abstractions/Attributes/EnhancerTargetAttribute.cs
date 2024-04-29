using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Field)]
public class EnhancerTargetAttribute(StandardValueType valueType) : Attribute
{
    public StandardValueType ValueType { get; } = valueType;
}