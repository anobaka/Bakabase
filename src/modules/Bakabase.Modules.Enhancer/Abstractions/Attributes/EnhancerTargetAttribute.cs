using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Abstractions.Attributes;

public class EnhancerTargetAttribute(StandardValueType valueType) : Attribute
{
    public StandardValueType ValueType { get; } = valueType;
}