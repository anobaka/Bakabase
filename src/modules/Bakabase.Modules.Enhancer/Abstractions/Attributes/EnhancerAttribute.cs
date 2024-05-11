using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;
using Bootstrap.Extensions;

namespace Bakabase.Modules.Enhancer.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Field)]
public class EnhancerAttribute(
    CustomPropertyValueScope customPropertyValueLayer,
    Type targetEnumType,
    Type? optionsType = null) : Attribute
{
    public Type TargetEnumType { get; } = targetEnumType;
    public Type OptionsType { get; } = optionsType ?? SpecificTypeUtils<EnhancerTargetOptions>.Type;
    public int CustomPropertyValueLayer { get; } = (int) customPropertyValueLayer;
}