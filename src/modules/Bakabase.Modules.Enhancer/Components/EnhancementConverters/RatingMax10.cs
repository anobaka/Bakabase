using Bakabase.Modules.CustomProperty.Components.Properties.Number;
using Bakabase.Modules.Enhancer.Abstractions.Components;

namespace Bakabase.Modules.Enhancer.Components.EnhancementConverters;

public class RatingMax10 : AbstractEnhancementConverter<decimal?, RatingProperty>
{
    protected override decimal? Convert(decimal? rawValue, RatingProperty targetProperty)
    {
        if (rawValue.HasValue)
        {
            var targetMax = targetProperty.Options?.MaxValue ?? RatingPropertyOptions.DefaultMaxValue;
            if (targetMax != 10)
            {
                return rawValue.Value / 10 * targetMax;
            }
        }

        return rawValue;
    }
}