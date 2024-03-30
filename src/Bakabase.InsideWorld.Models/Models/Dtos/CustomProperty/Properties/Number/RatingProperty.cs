using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstractions;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Properties.Number;

public record RatingPropertyOptions
{
    public int MaxValue { get; set; }
}

public record RatingProperty() : CustomPropertyDto<RatingPropertyOptions>;

public record RatingPropertyValue : CustomPropertyValueDto<decimal>
{
    protected override bool IsMatch(decimal value, CustomPropertyValueSearchRequestModel model)
    {
        throw new System.NotImplementedException();
    }
}

public class RatingPropertyDescriptor : AbstractCustomPropertyDescriptor<RatingProperty, RatingPropertyOptions,
    RatingPropertyValue, decimal>
{
    public override CustomPropertyType Type => CustomPropertyType.Rating;

    protected override bool IsMatch(decimal value, CustomPropertyValueSearchRequestModel model)
    {
        throw new System.NotImplementedException();
    }
}