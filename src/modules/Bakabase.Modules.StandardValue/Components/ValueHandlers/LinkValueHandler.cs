using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bootstrap.Extensions;

namespace Bakabase.Modules.StandardValue.Components.ValueHandlers
{
    public class LinkValueHandler : AbstractStandardValueHandler<LinkValue>
    {
        public override StandardValueType Type => StandardValueType.Link;

        protected override string? BuildDisplayValue(LinkValue value)
        {
            return value.ToString();
        }

        protected override bool ConvertToOptimizedTypedValue(object? currentValue, out LinkValue? optimizedTypedValue)
        {
            if (currentValue is LinkValue ld)
            {
                var trimText = ld.Text?.Trim();
                var trimUrl = ld.Url?.Trim();
                if (trimText != ld.Text || trimUrl != ld.Url)
                {
                    ld = new LinkValue(trimText, trimUrl);
                }

                if (!ld.IsEmpty)
                {
                    optimizedTypedValue = ld;
                    return true;
                }
            }

            optimizedTypedValue = default;
            return false;
        }

        public override string? ConvertToString(LinkValue optimizedValue) => optimizedValue.ToString();

        public override List<string>? ConvertToListString(LinkValue optimizedValue) =>
            optimizedValue.Url.IsNullOrEmpty()
                ? optimizedValue.Text.ConvertToListString()
                : [optimizedValue.ToString()!];

        public override decimal? ConvertToNumber(LinkValue optimizedValue) => optimizedValue.Text.ConvertToDecimal();
        public override bool? ConvertToBoolean(LinkValue optimizedValue) => optimizedValue.Text.ConvertToBoolean();
        public override LinkValue? ConvertToLink(LinkValue optimizedValue) => optimizedValue;

        public override List<List<string>>? ConvertToListListString(LinkValue optimizedValue) =>
            optimizedValue.Url.IsNullOrEmpty()
                ? optimizedValue.Text.ConvertToListListString()
                : [[optimizedValue.ToString()!]];

        protected override List<string>? ExtractTextsForConvertingToDateTimeInternal(LinkValue optimizedValue) =>
            optimizedValue.Text.IsNullOrEmpty() ? null : [optimizedValue.Text];

        protected override List<string>? ExtractTextsForConvertingToTime(LinkValue optimizedValue) =>
            optimizedValue.Text.IsNullOrEmpty() ? null : [optimizedValue.Text];

        public override List<TagValue>? ConvertToListTag(LinkValue optimizedValue) =>
            optimizedValue.Text.ConvertToListTag();
    }
}