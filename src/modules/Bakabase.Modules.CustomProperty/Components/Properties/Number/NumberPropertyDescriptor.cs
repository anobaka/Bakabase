using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Components.Properties.Number.Abstractions;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Number;

public class NumberPropertyDescriptor : NumberPropertyDescriptor<NumberProperty, NumberPropertyOptions, NumberPropertyValue>
{
    public override CustomPropertyType EnumType => CustomPropertyType.Number;
}