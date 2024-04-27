using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.StandardValue;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.StandardValue.ValueHandlers
{
    public class StringValueHandler(ISpecialTextService specialTextService) : AbstractStandardValueHandler<string>
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
            var date = await specialTextService.TryToParseDateTime(currentValue);
            return (date, date.HasValue ? null : StandardValueConversionLoss.All);
        }

        public override (string? NewValue, StandardValueConversionLoss? Loss) ConvertToFormula(string currentValue) =>
            (null, StandardValueConversionLoss.All);

        public override (LinkData? NewValue, StandardValueConversionLoss? Loss) ConvertToLink(string currentValue) =>
            (new LinkData { Text = currentValue, Url = currentValue }, null);

        public override (List<List<string>>? NewValue, StandardValueConversionLoss? Loss)
            ConvertToMultilevel(string currentValue) => ([[currentValue]], null);

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