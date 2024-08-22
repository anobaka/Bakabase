using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Abstractions.Models;
using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Components.Properties.Number.Abstractions;
using Bakabase.Modules.StandardValue.Abstractions.Components;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Number;

public record RatingPropertyOptions
{
    public const int DefaultMaxValue = 5;
    public int MaxValue { get; set; } = DefaultMaxValue;
}

public record RatingProperty() : CustomProperty<RatingPropertyOptions>;

public record RatingPropertyValue : CustomPropertyValue<decimal?>;

public class RatingPropertyDescriptor(IStandardValueHelper standardValueHelper)
    : NumberPropertyDescriptor<RatingProperty, RatingPropertyOptions,
        RatingPropertyValue>(standardValueHelper)
{
    public override CustomPropertyType EnumType => CustomPropertyType.Rating;
}