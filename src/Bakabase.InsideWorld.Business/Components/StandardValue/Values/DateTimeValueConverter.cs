using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.StandardValue.Abstractions;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.CustomProperty.Properties.Text;

namespace Bakabase.InsideWorld.Business.Components.StandardValue.Values
{
    public class DateTimeValueConverter : AbstractStandardValueHandler<DateTime>
    {
        private const string Template = "yyyy-MM-dd HH:mm:ss";

        public override StandardValueType Type => StandardValueType.DateTime;

        public override Dictionary<StandardValueType, StandardValueConversionLoss?> DefaultConversionLoss { get; } =
            new()
            {
                {StandardValueType.SingleLineText, null},
                {StandardValueType.MultilineText, null},
                {StandardValueType.Link, null},
                {StandardValueType.SingleChoice, null},
                {StandardValueType.MultipleChoice, null},
                {StandardValueType.Number, StandardValueConversionLoss.All},
                {StandardValueType.Percentage, StandardValueConversionLoss.All},
                {StandardValueType.Rating, StandardValueConversionLoss.All},
                {StandardValueType.Boolean, StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue},
                {StandardValueType.Attachment, StandardValueConversionLoss.All},
                {StandardValueType.Date, StandardValueConversionLoss.TimeWillBeLost},
                {StandardValueType.DateTime, null},
                {StandardValueType.Time, StandardValueConversionLoss.DateWillBeLost},
                {StandardValueType.Formula, StandardValueConversionLoss.All},
                {StandardValueType.Multilevel, StandardValueConversionLoss.All},
            };

        protected override DateTime ConvertToTypedValue(object? currentValue)
        {
            return currentValue is DateTime dt ? dt : default;
        }

        public override (string? NewValue, StandardValueConversionLoss? Loss) ConvertToString(DateTime currentValue)
        {
            return (currentValue.ToString(Template), null);
        }

        public override (List<string>? NewValue, StandardValueConversionLoss? Loss) ConvertToListString(
            DateTime currentValue)
        {
            return ([currentValue.ToString(Template)], null);
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
            var str = currentValue.ToString(Template);
            return (new LinkData { Url = str, Text = str }, null);
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
            return ([[currentValue.ToString(Template)]], null);
        }
    }
}