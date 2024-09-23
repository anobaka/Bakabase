using System.Globalization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.StandardValue.Components.ValueHandlers
{
    public class DecimalValueConverter(ICustomDateTimeParser customDateTimeParser)
        : AbstractStandardValueHandler<decimal>(customDateTimeParser)
    {
        public override StandardValueType Type => StandardValueType.Decimal;
        public override bool? ConvertToBoolean(decimal optimizedValue) => optimizedValue != 0;

        public override LinkValue? ConvertToLink(decimal optimizedValue) =>
            new(optimizedValue.ToString(CultureInfo.InvariantCulture), null);

        public override List<string>? ConvertToListString(decimal optimizedValue) =>
            [optimizedValue.ToString(CultureInfo.InvariantCulture)];

        public override List<List<string>>? ConvertToListListString(decimal optimizedValue) =>
            [[optimizedValue.ToString(CultureInfo.InvariantCulture)]];

        public override List<TagValue>? ConvertToListTag(decimal optimizedValue) =>
        [
            new TagValue(null, optimizedValue.ToString(CultureInfo.InvariantCulture))
        ];

        public override decimal? ConvertToNumber(decimal optimizedValue) => optimizedValue;

        protected override bool ConvertToOptimizedTypedValue(object? currentValue, out decimal optimizedTypedValue)
        {
            if (currentValue is decimal d)
            {
                optimizedTypedValue = d;
                return true;
            }

            optimizedTypedValue = default;
            return false;
        }

        public override string? ConvertToString(decimal optimizedValue) =>
            optimizedValue.ToString(CultureInfo.InvariantCulture);
    }
}