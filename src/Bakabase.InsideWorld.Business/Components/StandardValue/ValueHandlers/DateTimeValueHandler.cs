using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.StandardValue;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.StandardValue.ValueHandlers
{
    public class DateTimeValueHandler : AbstractStandardValueHandler<DateTime>
    {
        private const string Template = "yyyy-MM-dd HH:mm:ss";

        public override StandardValueType Type => StandardValueType.DateTime;

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
            };

        protected override string BuildDisplayValue(DateTime value)
        {
            return value.ToString(Template);
        }

        protected override DateTime ConvertToTypedValue(object? currentValue)
        {
            return currentValue is DateTime dt ? dt : default;
        }

        public override (string? NewValue, StandardValueConversionLoss? Loss) ConvertToString(DateTime currentValue)
        {
            return (BuildDisplayValue(currentValue), null);
        }

        public override (List<string>? NewValue, StandardValueConversionLoss? Loss) ConvertToListString(
            DateTime currentValue)
        {
            return ([BuildDisplayValue(currentValue)], null);
        }

        public override (decimal? NewValue, StandardValueConversionLoss? Loss) ConvertToNumber(DateTime currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (bool? NewValue, StandardValueConversionLoss? Loss) ConvertToBoolean(DateTime currentValue)
        {
            return (true, StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue);
        }

        public override (LinkData? NewValue, StandardValueConversionLoss? Loss) ConvertToLink(DateTime currentValue)
        {
            var str = BuildDisplayValue(currentValue);
            return (new LinkData {Url = str, Text = str}, null);
        }

        public override async Task<(DateTime? NewValue, StandardValueConversionLoss? Loss)> ConvertToDateTime(
            DateTime currentValue)
        {
            return (currentValue, null);
        }

        public override (TimeSpan? NewValue, StandardValueConversionLoss? Loss) ConvertToTime(DateTime currentValue)
        {
            return (currentValue.TimeOfDay, StandardValueConversionLoss.DateWillBeLost);
        }

        public override (string? NewValue, StandardValueConversionLoss? Loss) ConvertToFormula(DateTime currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (List<List<string>>? NewValue, StandardValueConversionLoss? Loss) ConvertToMultilevel(
            DateTime currentValue)
        {
            return ([[BuildDisplayValue(currentValue)]], null);
        }
    }
}