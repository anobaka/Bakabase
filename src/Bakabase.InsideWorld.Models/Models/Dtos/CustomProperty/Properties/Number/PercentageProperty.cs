using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstractions;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Properties.Number;

public record PercentagePropertyOptions
{
    public int Precision { get; set; }
    public bool ShowProgressBar { get; set; }
}

public record PercentageProperty() : CustomPropertyDto<PercentagePropertyOptions>;

public record PercentagePropertyValue : CustomPropertyValueDto<decimal>
{
    protected override bool IsMatch(decimal value, CustomPropertyValueSearchRequestModel model)
    {
        throw new System.NotImplementedException();
    }
}

public class PercentagePropertyDescriptor : AbstractCustomPropertyDescriptor<PercentageProperty, PercentagePropertyOptions,
    PercentagePropertyValue, decimal>
{
    public override CustomPropertyType Type => CustomPropertyType.Percentage;

    protected override bool IsMatch(decimal value, CustomPropertyValueSearchRequestModel model)
    {
        throw new System.NotImplementedException();
    }
}