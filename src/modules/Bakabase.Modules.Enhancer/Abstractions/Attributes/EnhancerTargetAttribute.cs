using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Field)]
public class EnhancerTargetAttribute(StandardValueType valueType, EnhancerTargetOptionsItem[]? options = null) : Attribute
{
    public StandardValueType ValueType { get; } = valueType;
    public EnhancerTargetOptionsItem[]? Options { get; } = options;
}