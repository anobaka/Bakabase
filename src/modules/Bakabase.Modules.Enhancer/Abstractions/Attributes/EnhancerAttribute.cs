using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Field)]
public class EnhancerAttribute(
    Type enhancerType,
    PropertyValueScope propertyValueScope,
    Type targetEnumType
) : Attribute
{
    public Type EnhancerType { get; } = enhancerType;
    public Type TargetEnumType { get; } = targetEnumType;
    public int PropertyValueScope { get; } = (int) propertyValueScope;
}