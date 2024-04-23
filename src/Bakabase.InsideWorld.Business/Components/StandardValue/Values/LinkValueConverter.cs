using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.StandardValue.Abstractions;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.CustomProperty.Properties.Text;

namespace Bakabase.InsideWorld.Business.Components.StandardValue.Values
{
    public class LinkValueConverter : AbstractStandardValueHandler<LinkData>
    {
        public override StandardValueType Type => StandardValueType.Link;

        public override Dictionary<StandardValueType, StandardValueConversionLoss?> DefaultConversionLoss { get; } =
            new()
            {
                {StandardValueType.SingleLineText, StandardValueConversionLoss.TextWillBeLost},
                {StandardValueType.MultilineText, StandardValueConversionLoss.TextWillBeLost},
                {StandardValueType.Link, null},
                {StandardValueType.SingleChoice, StandardValueConversionLoss.TextWillBeLost},
                {StandardValueType.MultipleChoice, StandardValueConversionLoss.TextWillBeLost},
                {StandardValueType.Number, StandardValueConversionLoss.All},
                {StandardValueType.Percentage, StandardValueConversionLoss.All},
                {StandardValueType.Rating, StandardValueConversionLoss.All},
                {StandardValueType.Boolean, StandardValueConversionLoss.All},
                {StandardValueType.Attachment, StandardValueConversionLoss.TextWillBeLost},
                {StandardValueType.Date, StandardValueConversionLoss.All},
                {StandardValueType.DateTime, StandardValueConversionLoss.All},
                {StandardValueType.Time, StandardValueConversionLoss.All},
                {StandardValueType.Formula, StandardValueConversionLoss.All},
                {StandardValueType.MultilevelText, StandardValueConversionLoss.All},
            };

        protected override LinkData? ConvertToTypedValue(object? currentValue)
        {
            var ld = currentValue as LinkData;
            return string.IsNullOrEmpty(ld?.Text) && string.IsNullOrEmpty(ld?.Url) ? null : ld;
        }

        public override (string? NewValue, StandardValueConversionLoss? Loss) ConvertToString(LinkData currentValue)
        {
            if (!string.IsNullOrEmpty(currentValue.Url))
            {
                return (currentValue.Url,
                    string.IsNullOrEmpty(currentValue.Text) ? null : StandardValueConversionLoss.TextWillBeLost);
            }

            return (currentValue.Text, null);
        }

        public override (List<string>? NewValue, StandardValueConversionLoss? Loss) ConvertToListString(
            LinkData currentValue)
        {
            var (nv, loss) = ConvertToString(currentValue);
            return (nv == null ? null : [nv], loss);
        }

        public override (decimal? NewValue, StandardValueConversionLoss? Loss) ConvertToNumber(LinkData currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (bool? NewValue, StandardValueConversionLoss? Loss) ConvertToBoolean(LinkData currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (LinkData? NewValue, StandardValueConversionLoss? Loss) ConvertToLink(LinkData currentValue) =>
            (currentValue, null);

        public override async Task<(DateTime? NewValue, StandardValueConversionLoss? Loss)> ConvertToDateTime(
            LinkData currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (TimeSpan? NewValue, StandardValueConversionLoss? Loss) ConvertToTime(LinkData currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (string? NewValue, StandardValueConversionLoss? Loss) ConvertToFormula(LinkData currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (List<List<string>>? NewValue, StandardValueConversionLoss? Loss) ConvertToMultilevel(
            LinkData currentValue)
        {
            var (nv, loss) = ConvertToString(currentValue);
            return (nv == null ? null : [[nv]], loss);
        }
    }
}