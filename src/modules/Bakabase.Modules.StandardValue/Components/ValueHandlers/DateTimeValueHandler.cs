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
    public class DateTimeValueHandler : AbstractStandardValueHandler<DateTime>
    {
        private const string Template = "yyyy-MM-dd HH:mm:ss";

        public override StandardValueType Type => StandardValueType.DateTime;

        protected override string BuildDisplayValue(DateTime value)
        {
            return value.ToString(Template);
        }

        protected override bool ConvertToOptimizedTypedValue(object? currentValue, out DateTime optimizedTypedValue)
        {
            if (currentValue is DateTime dt)
            {
                optimizedTypedValue = dt;
                return true;
            }

            optimizedTypedValue = default;
            return false;
        }

        public override string? ConvertToString(DateTime optimizedValue) => optimizedValue.ToString(Template);

        public override List<string>? ConvertToListString(DateTime optimizedValue) =>
            [ConvertToString(optimizedValue)!];

        public override decimal? ConvertToNumber(DateTime optimizedValue) => null;

        public override bool? ConvertToBoolean(DateTime optimizedValue) => true;

        public override LinkValue? ConvertToLink(DateTime optimizedValue) =>
            new LinkValue(ConvertToString(optimizedValue)!, null);

        public override List<List<string>>? ConvertToListListString(DateTime optimizedValue) =>
            [[ConvertToString(optimizedValue)!]];

        public override List<TagValue>? ConvertToListTag(DateTime optimizedValue) =>
            [new TagValue(null, ConvertToString(optimizedValue)!)];

        public override DateTime? ConvertToDateTime(DateTime optimizedValue) => optimizedValue;

        public override TimeSpan? ConvertToTime(DateTime optimizedValue) => optimizedValue.TimeOfDay;
    }
}