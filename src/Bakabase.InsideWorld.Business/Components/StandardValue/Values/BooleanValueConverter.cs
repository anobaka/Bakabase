using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.StandardValue.Abstractions;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.CustomProperty.Properties.Text;

namespace Bakabase.InsideWorld.Business.Components.StandardValue.Values
{
    public class BooleanValueConverter : AbstractStandardValueHandler<bool>
    {
        public override StandardValueType Type => StandardValueType.Boolean;

        public override Dictionary<StandardValueType, StandardValueConversionLoss?> DefaultConversionLoss { get; } =
            new()
            {
                {StandardValueType.SingleLineText, null},
                {StandardValueType.MultilineText, null},
                {StandardValueType.Link, StandardValueConversionLoss.All},
                {StandardValueType.SingleChoice, null},
                {StandardValueType.MultipleChoice, null},
                {StandardValueType.Number, null},
                {StandardValueType.Percentage, null},
                {StandardValueType.Rating, null},
                {StandardValueType.Boolean, null},
                {StandardValueType.Attachment, StandardValueConversionLoss.All},
                {StandardValueType.Date, StandardValueConversionLoss.All},
                {StandardValueType.DateTime, StandardValueConversionLoss.All},
                {StandardValueType.Time, StandardValueConversionLoss.All},
                {StandardValueType.Formula, StandardValueConversionLoss.All},
                {StandardValueType.MultilevelText, StandardValueConversionLoss.All},
            };

        protected override bool ConvertToTypedValue(object? currentValue) => currentValue is true;

        public override (string? NewValue, StandardValueConversionLoss? Loss) ConvertToString(bool currentValue)
        {
            return ((currentValue ? 1 : 0).ToString(), null);
        }

        public override (List<string>? NewValue, StandardValueConversionLoss? Loss) ConvertToListString(
            bool currentValue)
        {
            return ([(currentValue ? 1 : 0).ToString()], null);
        }

        public override (decimal? NewValue, StandardValueConversionLoss? Loss) ConvertToNumber(bool currentValue)
        {
            return (currentValue ? 1 : 0, null);
        }

        public override (bool? NewValue, StandardValueConversionLoss? Loss) ConvertToBoolean(bool currentValue)
        {
            return (currentValue, null);
        }

        public override (LinkData? NewValue, StandardValueConversionLoss? Loss) ConvertToLink(bool currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override async Task<(DateTime? NewValue, StandardValueConversionLoss? Loss)> ConvertToDateTime(
            bool currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (TimeSpan? NewValue, StandardValueConversionLoss? Loss) ConvertToTime(bool currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (string? NewValue, StandardValueConversionLoss? Loss) ConvertToFormula(bool currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (List<List<string>>? NewValue, StandardValueConversionLoss? Loss) ConvertToMultilevel(
            bool currentValue)
        {
            return ([[(currentValue ? 1 : 0).ToString()]], null);
        }
    }
}