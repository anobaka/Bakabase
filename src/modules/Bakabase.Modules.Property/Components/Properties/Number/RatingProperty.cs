using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Models;
using Bakabase.Modules.Property.Components.Properties.Number.Abstractions;

namespace Bakabase.Modules.Property.Components.Properties.Number;

public record RatingPropertyOptions
{
    public const int DefaultMaxValue = 5;
    public int MaxValue { get; set; } = DefaultMaxValue;
}

public class RatingPropertyDescriptor : NumberPropertyDescriptor<RatingPropertyOptions>
{
    public override PropertyType Type => PropertyType.Rating;
}