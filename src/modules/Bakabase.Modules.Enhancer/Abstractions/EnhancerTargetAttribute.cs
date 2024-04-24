using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Components.Enhancer;

public class EnhancerTargetAttribute(StandardValueType valueType) : Attribute
{
    public StandardValueType ValueType { get; } = valueType;
}