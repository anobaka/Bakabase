using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Configurations;
using Bakabase.Modules.StandardValue.Abstractions.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.StandardValue.Components.ValueHandlers
{
    public class ListStringValueHandler : AbstractStandardValueHandler<List<string>>
    {
        public override StandardValueType Type => StandardValueType.ListString;

        protected override string? BuildDisplayValue(List<string> value)
        {
            var goodValues = value.TrimAndRemoveEmpty();
            return goodValues?.Any() == true ? string.Join(InternalOptions.TextSeparator, goodValues) : null;
        }

        protected override bool ConvertToOptimizedTypedValue(object? currentValue,
            out List<string>? optimizedTypedValue)
        {
            if (currentValue is List<string> list)
            {
                var goodValues = list.TrimAndRemoveEmpty()?.ToList();
                if (goodValues?.Any() == true)
                {
                    optimizedTypedValue = goodValues;
                    return true;
                }
            }

            optimizedTypedValue = default;
            return false;
        }

        public override string? ConvertToString(List<string> optimizedValue)
        {
            return optimizedValue.Any()
                ? string.Join(StandardValueInternals.CommonListItemSeparator, optimizedValue)
                : null;
        }

        public override List<string>? ConvertToListString(List<string> optimizedValue) => optimizedValue;

        public override decimal? ConvertToNumber(List<string> optimizedValue) =>
            optimizedValue.FirstNotNullOrDefault(t => t.ConvertToDecimal());

        public override bool? ConvertToBoolean(List<string> optimizedValue) => optimizedValue.Count > 1
            ? true
            : optimizedValue.FirstNotNullOrDefault(t => t.ConvertToBoolean());

        public override LinkValue? ConvertToLink(List<string> optimizedValue)
        {
            if (optimizedValue.Count == 1)
            {
                var lv = optimizedValue[0].ConvertToLinkValue();
                if (lv != null)
                {
                    return lv;
                }
            }

            return new LinkValue(string.Join(StandardValueInternals.CommonListItemSeparator, optimizedValue), null);
        }

        protected override List<string>? ExtractTextsForConvertingToDateTimeInternal(List<string> optimizedValue) =>
            optimizedValue;

        protected override List<string>? ExtractTextsForConvertingToTime(List<string> optimizedValue) => optimizedValue;

        public override List<List<string>>? ConvertToListListString(List<string> optimizedValue) => optimizedValue
            .Select(v => v.ConvertToInnerListOfListListString().ToNullIfEmpty()).OfType<List<string>>().ToList()
            .ToNullIfEmpty();

        public override List<TagValue>? ConvertToListTag(List<string> optimizedValue) =>
            optimizedValue.Select(TagValue.TryParse).OfType<TagValue>().ToList().ToNullIfEmpty();
    }
}