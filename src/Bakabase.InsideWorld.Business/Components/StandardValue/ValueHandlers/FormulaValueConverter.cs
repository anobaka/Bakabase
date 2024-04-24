using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.StandardValue.Abstractions;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.CustomProperty.Properties.Text;

namespace Bakabase.InsideWorld.Business.Components.StandardValue.Values
{
    public class FormulaValueConverter : AbstractStandardValueHandler<string>
    {
        public override StandardValueType Type => StandardValueType.Formula;

        public override Dictionary<StandardValueType, StandardValueConversionLoss?> DefaultConversionLoss { get; } =
            new()
            {
                {StandardValueType.SingleLineText, StandardValueConversionLoss.All},
                {StandardValueType.MultilineText, StandardValueConversionLoss.All},
                {StandardValueType.Link, StandardValueConversionLoss.All},
                {StandardValueType.SingleTextChoice, StandardValueConversionLoss.All},
                {StandardValueType.MultipleTextChoice, StandardValueConversionLoss.All},
                {StandardValueType.Number, StandardValueConversionLoss.All},
                {StandardValueType.Percentage, StandardValueConversionLoss.All},
                {StandardValueType.Rating, StandardValueConversionLoss.All},
                {StandardValueType.Boolean, StandardValueConversionLoss.All},
                {StandardValueType.Attachment, StandardValueConversionLoss.All},
                {StandardValueType.Date, StandardValueConversionLoss.All},
                {StandardValueType.DateTime, StandardValueConversionLoss.All},
                {StandardValueType.Time, StandardValueConversionLoss.All},
                {StandardValueType.Formula, null},
                {StandardValueType.MultipleTextTree, StandardValueConversionLoss.All},
            };

        protected override string? ConvertToTypedValue(object? currentValue)
        {
            var str = currentValue as string;
            return !string.IsNullOrEmpty(str) ? str : null;
        }

        public override (string? NewValue, StandardValueConversionLoss? Loss) ConvertToString(string currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (List<string>? NewValue, StandardValueConversionLoss? Loss) ConvertToListString(
            string currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (decimal? NewValue, StandardValueConversionLoss? Loss) ConvertToNumber(string currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (bool? NewValue, StandardValueConversionLoss? Loss) ConvertToBoolean(string currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (LinkData? NewValue, StandardValueConversionLoss? Loss) ConvertToLink(string currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override async Task<(DateTime? NewValue, StandardValueConversionLoss? Loss)> ConvertToDateTime(
            string currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (TimeSpan? NewValue, StandardValueConversionLoss? Loss) ConvertToTime(string currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (string? NewValue, StandardValueConversionLoss? Loss) ConvertToFormula(string currentValue)
        {
            return (currentValue, null);
        }

        public override (List<List<string>>? NewValue, StandardValueConversionLoss? Loss) ConvertToMultilevel(
            string currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }
    }
}