using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Abstractions.Attributes;

public class EnhancerAttribute(CustomPropertyValueLayer customPropertyValueLayer) : Attribute
{
    public CustomPropertyValueLayer CustomPropertyValueLayer { get; } = customPropertyValueLayer;
}