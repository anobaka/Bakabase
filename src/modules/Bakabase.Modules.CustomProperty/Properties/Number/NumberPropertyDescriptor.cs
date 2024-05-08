using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Properties.Number.Abstractions;

namespace Bakabase.Modules.CustomProperty.Properties.Number;

public class NumberPropertyDescriptor : NumberPropertyDescriptor<NumberProperty, NumberPropertyOptions, NumberPropertyValue>
{
    public override CustomPropertyType EnumType => CustomPropertyType.Number;
}