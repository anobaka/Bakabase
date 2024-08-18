using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Components.Properties.Number.Abstractions;
using Bakabase.Modules.CustomProperty.Models;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Number;

public record RatingPropertyOptions
{
    public int MaxValue { get; set; }
}

public record RatingProperty() : CustomProperty<RatingPropertyOptions>;

public record RatingPropertyValue : CustomPropertyValue<decimal>;

public class RatingPropertyDescriptor(IStandardValueHelper standardValueHelper) : NumberPropertyDescriptor<RatingProperty, RatingPropertyOptions,
    RatingPropertyValue>(standardValueHelper)
{
    public override CustomPropertyType EnumType => CustomPropertyType.Rating;
}