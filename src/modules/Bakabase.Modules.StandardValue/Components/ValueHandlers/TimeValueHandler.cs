using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.StandardValue.Components.ValueHandlers
{
    public class TimeValueHandler : AbstractStandardValueHandler<TimeSpan>
    {
        private const string Template = "g";

        public override StandardValueType Type => StandardValueType.Time;

        public override Dictionary<StandardValueType, StandardValueConversionLoss?> DefaultConversionLoss { get; } =
            new()
            {
                {StandardValueType.String, null},
                {StandardValueType.ListString, null},
                {StandardValueType.Decimal, StandardValueConversionLoss.All},
                {StandardValueType.Link, null},
                {StandardValueType.Boolean, StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue},
                {StandardValueType.DateTime, null},
                {StandardValueType.Time, StandardValueConversionLoss.DateWillBeLost},
                {StandardValueType.ListListString, StandardValueConversionLoss.All},
                {StandardValueType.ListTag, StandardValueConversionLoss.All}
            };

        protected override string BuildDisplayValue(TimeSpan value)
        {
            return value.ToString(Template);
        }

        protected override TimeSpan ConvertToTypedValue(object? currentValue)
        {
            return currentValue is TimeSpan dt ? dt : default;
        }

        public override (string? NewValue, StandardValueConversionLoss? Loss) ConvertToString(TimeSpan currentValue)
        {
            return (BuildDisplayValue(currentValue), null);
        }

        public override (List<string>? NewValue, StandardValueConversionLoss? Loss) ConvertToListString(
            TimeSpan currentValue)
        {
            return ([BuildDisplayValue(currentValue)], null);
        }

        public override (decimal? NewValue, StandardValueConversionLoss? Loss) ConvertToNumber(TimeSpan currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (bool? NewValue, StandardValueConversionLoss? Loss) ConvertToBoolean(TimeSpan currentValue)
        {
            return (true, StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue);
        }

        public override (LinkValue? NewValue, StandardValueConversionLoss? Loss) ConvertToLink(TimeSpan currentValue)
        {
            var str = BuildDisplayValue(currentValue);
            return (new LinkValue { Url = str, Text = str }, null);
        }

        public override Task<(DateTime? NewValue, StandardValueConversionLoss? Loss)> ConvertToDateTime(
            TimeSpan currentValue)
        {
            return Task.FromResult<(DateTime? NewValue, StandardValueConversionLoss? Loss)>((null,
                StandardValueConversionLoss.All));
        }

        public override (TimeSpan? NewValue, StandardValueConversionLoss? Loss) ConvertToTime(TimeSpan currentValue)
        {
            return (currentValue, StandardValueConversionLoss.DateWillBeLost);
        }

        public override (List<List<string>>? NewValue, StandardValueConversionLoss? Loss) ConvertToMultilevel(
            TimeSpan currentValue)
        {
            return ([[BuildDisplayValue(currentValue)]], null);
        }

        public override (List<TagValue>? NewValue, StandardValueConversionLoss? Loss) ConvertToListTag(TimeSpan currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }
    }
}