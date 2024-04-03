using Bakabase.Abstractions.Components.CustomProperty;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.Modules.CustomProperty.Properties.Number;

public record NumberPropertyOptions
{
    public int Precision { get; set; }
}

public record NumberProperty(): CustomProperty<NumberPropertyOptions>;

public record NumberPropertyValue: CustomPropertyValueDto<decimal>
{
    protected override bool IsMatch(decimal value, CustomPropertyValueSearchRequestModel model)
    {
        throw new System.NotImplementedException();
    }
}

public class NumberPropertyDescriptor : AbstractCustomPropertyDescriptor<NumberProperty, NumberPropertyOptions,
    NumberPropertyValue, decimal>
{
    public override CustomPropertyType Type => CustomPropertyType.Number;

    protected override bool IsMatch(decimal value, CustomPropertyValueSearchRequestModel model)
    {
        throw new System.NotImplementedException();
    }
}