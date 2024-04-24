using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.StandardValue.Abstractions;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.CustomProperty.Properties.Text;

namespace Bakabase.InsideWorld.Business.Components.StandardValue.Values
{
    public class TimeValueConverter : AbstractStandardValueHandler<TimeSpan>
    {
        private const string Template = "g";

        public override StandardValueType Type => StandardValueType.Time;

        public override Dictionary<StandardValueType, StandardValueConversionLoss?> DefaultConversionLoss { get; } =
            new()
            {
                {StandardValueType.SingleLineText, null},
                {StandardValueType.MultilineText, null},
                {StandardValueType.Link, null},
                {StandardValueType.SingleTextChoice, null},
                {StandardValueType.MultipleTextChoice, null},
                {StandardValueType.Number, StandardValueConversionLoss.All},
                {StandardValueType.Percentage, StandardValueConversionLoss.All},
                {StandardValueType.Rating, StandardValueConversionLoss.All},
                {StandardValueType.Boolean, StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue},
                {StandardValueType.Attachment, StandardValueConversionLoss.All},
                {StandardValueType.Date, StandardValueConversionLoss.TimeWillBeLost},
                {StandardValueType.DateTime, null},
                {StandardValueType.Time, StandardValueConversionLoss.DateWillBeLost},
                {StandardValueType.Formula, StandardValueConversionLoss.All},
                {StandardValueType.MultipleTextTree, StandardValueConversionLoss.All},
            };

        protected override TimeSpan ConvertToTypedValue(object? currentValue)
        {
            return currentValue is TimeSpan dt ? dt : default;
        }

        public override (string? NewValue, StandardValueConversionLoss? Loss) ConvertToString(TimeSpan currentValue)
        {
            return (currentValue.ToString(Template), null);
        }

        public override (List<string>? NewValue, StandardValueConversionLoss? Loss) ConvertToListString(
            TimeSpan currentValue)
        {
            return ([currentValue.ToString(Template)], null);
        }

        public override (decimal? NewValue, StandardValueConversionLoss? Loss) ConvertToNumber(TimeSpan currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (bool? NewValue, StandardValueConversionLoss? Loss) ConvertToBoolean(TimeSpan currentValue)
        {
            return (true, StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue);
        }

        public override (LinkData? NewValue, StandardValueConversionLoss? Loss) ConvertToLink(TimeSpan currentValue)
        {
            var str = currentValue.ToString(Template);
            return (new LinkData { Url = str, Text = str }, null);
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

        public override (string? NewValue, StandardValueConversionLoss? Loss) ConvertToFormula(TimeSpan currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (List<List<string>>? NewValue, StandardValueConversionLoss? Loss) ConvertToMultilevel(
            TimeSpan currentValue)
        {
            return ([[currentValue.ToString(Template)]], null);
        }
    }
}