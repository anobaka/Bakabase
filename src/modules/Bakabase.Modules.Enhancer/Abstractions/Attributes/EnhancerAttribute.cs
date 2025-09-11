using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Field)]
public class EnhancerAttribute(
    Type enhancerType,
    PropertyValueScope propertyValueScope,
    Type targetEnumType,
    EnhancerTag[] tags) : Attribute
{
    public Type EnhancerType { get; } = enhancerType;
    public Type TargetEnumType { get; } = targetEnumType;
    public int PropertyValueScope { get; } = (int) propertyValueScope;
    public EnhancerTag[] Tags { get; } = tags;
}