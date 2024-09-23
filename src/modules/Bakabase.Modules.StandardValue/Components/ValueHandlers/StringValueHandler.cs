using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bootstrap.Extensions;

namespace Bakabase.Modules.StandardValue.Components.ValueHandlers
{
    public class StringValueHandler(ICustomDateTimeParser customDateTimeParser)
        : AbstractStandardValueHandler<string>(customDateTimeParser)
    {
        public override StandardValueType Type => StandardValueType.String;

        protected override bool ConvertToOptimizedTypedValue(object? currentValue, out string? optimizedTypedValue)
        {
            var str = (currentValue as string)?.Trim();
            if (str.IsNullOrEmpty())
            {
                optimizedTypedValue = default;
                return false;
            }

            optimizedTypedValue = str;
            return true;
        }

        public override string? ConvertToString(string optimizedValue) => optimizedValue;

        public override List<string>? ConvertToListString(string optimizedValue) =>
            optimizedValue.ConvertToListString();

        public override bool? ConvertToBoolean(string optimizedValue) => optimizedValue.ConvertToBoolean();


        public override LinkValue? ConvertToLink(string optimizedValue) => optimizedValue.ConvertToLinkValue();

        protected override List<string>? ExtractTextsForConvertingToDateTime(string optimizedValue) =>
            [optimizedValue];

        protected override List<string>? ExtractTextsForConvertingToTime(string optimizedValue) => [optimizedValue];

        public override List<List<string>>? ConvertToListListString(string optimizedValue) =>
            optimizedValue.ConvertToListListString();

        public override List<TagValue>? ConvertToListTag(string optimizedValue) => optimizedValue.ConvertToListTag();

        public override decimal? ConvertToNumber(string optimizedValue) => optimizedValue.ConvertToDecimal();

    }
}