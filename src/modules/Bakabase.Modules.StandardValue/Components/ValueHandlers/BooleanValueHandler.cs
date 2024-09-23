using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.StandardValue.Components.ValueHandlers
{
    public class BooleanValueHandler(ICustomDateTimeParser customDateTimeParser)
        : AbstractStandardValueHandler<bool>(customDateTimeParser)
    {
        public override StandardValueType Type => StandardValueType.Boolean;

        protected override string BuildDisplayValue(bool value) => value ? "1" : "0";

        protected override bool ConvertToOptimizedTypedValue(object? currentValue, out bool optimizedTypedValue)
        {
            if (currentValue is bool b)
            {
                optimizedTypedValue = b;
                return true;
            }

            optimizedTypedValue = default;
            return false;
        }

        public override string ConvertToString(bool optimizedValue) => optimizedValue ? "1" : "0";

        public override List<string> ConvertToListString(bool optimizedValue) => [ConvertToString(optimizedValue)!];

        public override decimal? ConvertToNumber(bool optimizedValue) => optimizedValue ? 1 : 0;

        public override bool? ConvertToBoolean(bool optimizedValue) => optimizedValue;

        public override LinkValue? ConvertToLink(bool optimizedValue) =>
            new LinkValue(ConvertToString(optimizedValue), null);

        public override List<List<string>>? ConvertToListListString(bool optimizedValue) =>
            [[ConvertToString(optimizedValue)]];

        public override List<TagValue>? ConvertToListTag(bool optimizedValue) => [new TagValue(null, ConvertToString(optimizedValue))];
    }
}