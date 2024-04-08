using Bakabase.Abstractions.Components.CustomProperty;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.Modules.CustomProperty.Properties.Number;

public record RatingPropertyOptions
{
    public int MaxValue { get; set; }
}

public record RatingProperty() : CustomProperty<RatingPropertyOptions>;

public record RatingPropertyValue : TypedCustomPropertyValue<decimal>
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