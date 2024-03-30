using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstractions;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Properties.Number;

public record NumberPropertyOptions
{
    public int Precision { get; set; }
}

public record NumberProperty(): CustomPropertyDto<NumberPropertyOptions>;

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