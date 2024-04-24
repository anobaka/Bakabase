using Bakabase.Abstractions.Components.CustomProperty;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Properties.Number.Abstractions;

namespace Bakabase.Modules.CustomProperty.Properties.Number;

public record RatingPropertyOptions
{
    public int MaxValue { get; set; }
}

public record RatingProperty() : CustomProperty<RatingPropertyOptions>;

public record RatingPropertyValue : CustomPropertyValue<decimal>;

public class RatingPropertyDescriptor : NumberPropertyDescriptor<RatingProperty, RatingPropertyOptions,
    RatingPropertyValue>
{
    public override CustomPropertyType Type => CustomPropertyType.Rating;
}