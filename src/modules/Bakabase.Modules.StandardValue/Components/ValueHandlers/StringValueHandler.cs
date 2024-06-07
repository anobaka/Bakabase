using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.StandardValue.Components.ValueHandlers
{
    public class StringValueHandler(IDateTimeParser dateTimeParser) : AbstractStandardValueHandler<string>
    {
        public override StandardValueType Type => StandardValueType.String;

        public override Dictionary<StandardValueType, StandardValueConversionLoss?> DefaultConversionLoss =>
            new Dictionary<StandardValueType, StandardValueConversionLoss?>
            {
                {StandardValueType.String, null},
                {StandardValueType.ListString, null},
                {StandardValueType.Decimal, StandardValueConversionLoss.InconvertibleDataWillBeLost},
                {StandardValueType.Link, null},
                {StandardValueType.Boolean, StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue},
                {StandardValueType.DateTime, StandardValueConversionLoss.InconvertibleDataWillBeLost},
                {StandardValueType.Time, StandardValueConversionLoss.InconvertibleDataWillBeLost},
                {StandardValueType.ListListString, null},
                {StandardValueType.ListTag, null}
            };

        protected override string? ConvertToTypedValue(object? currentValue)
        {
            var str = currentValue as string;
            return string.IsNullOrEmpty(str) ? null : str;
        }

        public override (string? NewValue, StandardValueConversionLoss? Loss) ConvertToString(string currentValue) =>
            (currentValue, null);

        public override (List<string>? NewValue, StandardValueConversionLoss? Loss)
            ConvertToListString(string currentValue) => ([currentValue], null);

        public override (bool? NewValue, StandardValueConversionLoss? Loss) ConvertToBoolean(string currentValue) =>
            (true, StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue);

        public override async Task<(DateTime? NewValue, StandardValueConversionLoss? Loss)> ConvertToDateTime(
            string currentValue)
        {
            var date = await dateTimeParser.TryToParseDateTime(currentValue);
            return (date, date.HasValue ? null : StandardValueConversionLoss.All);
        }

        public override (LinkValue? NewValue, StandardValueConversionLoss? Loss) ConvertToLink(string currentValue) =>
            (new LinkValue { Text = currentValue, Url = currentValue }, null);

        public override (List<List<string>>? NewValue, StandardValueConversionLoss? Loss)
            ConvertToMultilevel(string currentValue) => ([[currentValue]], null);

        public override (List<TagValue>? NewValue, StandardValueConversionLoss? Loss) ConvertToListTag(string currentValue)
        {
            return ([new TagValue(null, currentValue)], null);
        }

        public override (decimal? NewValue, StandardValueConversionLoss? Loss) ConvertToNumber(string currentValue)
        {
            return decimal.TryParse(currentValue, out var number)
                ? (number, null)
                : (null, StandardValueConversionLoss.All);
        }

        public override (TimeSpan? NewValue, StandardValueConversionLoss? Loss) ConvertToTime(string currentValue)
        {
            return TimeSpan.TryParse(currentValue, out var time)
                ? (time, null)
                : (null, StandardValueConversionLoss.All);
        }
    }
}