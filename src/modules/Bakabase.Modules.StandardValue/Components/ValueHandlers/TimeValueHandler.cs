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
    public class TimeValueHandler(ICustomDateTimeParser customDateTimeParser)
        : AbstractStandardValueHandler<TimeSpan>(customDateTimeParser)
    {
        private const string Template = "g";

        public override StandardValueType Type => StandardValueType.Time;

        protected override string BuildDisplayValue(TimeSpan value)
        {
            return value.ToString(Template);
        }

        protected override bool ConvertToOptimizedTypedValue(object? currentValue, out TimeSpan optimizedTypedValue)
        {
            if (currentValue is TimeSpan ts)
            {
                optimizedTypedValue = ts;
                return true;
            }

            optimizedTypedValue = default;
            return false;
        }

        public override string? ConvertToString(TimeSpan optimizedValue) => optimizedValue.ToString("g");

        public override List<string>? ConvertToListString(TimeSpan optimizedValue) =>
            [ConvertToString(optimizedValue)!];

        public override decimal? ConvertToNumber(TimeSpan optimizedValue) => null;

        public override bool? ConvertToBoolean(TimeSpan optimizedValue) => true;

        public override LinkValue? ConvertToLink(TimeSpan optimizedValue) =>
            new(ConvertToString(optimizedValue)!, null);

        public override List<List<string>>? ConvertToListListString(TimeSpan optimizedValue) =>
            [[ConvertToString(optimizedValue)!]];

        public override List<TagValue>? ConvertToListTag(TimeSpan optimizedValue) =>
            [new TagValue(null, BuildDisplayValue(optimizedValue))];

        public override TimeSpan? ConvertToTime(TimeSpan optimizedValue) => optimizedValue;

        public override Task<DateTime?> ConvertToDateTime(TimeSpan optimizedValue) =>
            Task.FromResult((DateTime?) DateTime.Now.Date.Add(optimizedValue));
    }
}