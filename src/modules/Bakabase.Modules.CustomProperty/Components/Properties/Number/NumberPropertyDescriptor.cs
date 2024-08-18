using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Components.Properties.Number.Abstractions;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Number;

public class NumberPropertyDescriptor(IStandardValueHelper standardValueHelper) : NumberPropertyDescriptor<NumberProperty, NumberPropertyOptions, NumberPropertyValue>(standardValueHelper)
{
    public override CustomPropertyType EnumType => CustomPropertyType.Number;
}