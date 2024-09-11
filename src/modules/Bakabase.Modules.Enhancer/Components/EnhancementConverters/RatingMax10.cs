using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.CustomProperty.Components.Properties.Number;
using Bakabase.Modules.Enhancer.Abstractions.Components;

namespace Bakabase.Modules.Enhancer.Components.EnhancementConverters;

public class RatingMax10 : AbstractEnhancementConverter<decimal?>
{
    protected override decimal? Convert(decimal? rawValue, Property property)
    {
        if (rawValue.HasValue)
        {
            var targetMax = (property.Options as RatingPropertyOptions)?.MaxValue ??
                            RatingPropertyOptions.DefaultMaxValue;
            if (targetMax != 10)
            {
                return rawValue.Value / 10 * targetMax;
            }
        }

        return rawValue;
    }
}