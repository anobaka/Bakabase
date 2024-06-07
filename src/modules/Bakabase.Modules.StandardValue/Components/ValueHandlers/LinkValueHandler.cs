using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.StandardValue.Components.ValueHandlers
{
    public class LinkValueHandler : AbstractStandardValueHandler<LinkValue>
    {
        public override StandardValueType Type => StandardValueType.Link;

        public override Dictionary<StandardValueType, StandardValueConversionLoss?> DefaultConversionLoss { get; } =
            new()
            {
                {StandardValueType.String, StandardValueConversionLoss.TextWillBeLost},
                {StandardValueType.ListString, StandardValueConversionLoss.TextWillBeLost},
                {StandardValueType.Decimal, StandardValueConversionLoss.All},
                {StandardValueType.Link, null},
                {StandardValueType.Boolean, StandardValueConversionLoss.All},
                {StandardValueType.DateTime, StandardValueConversionLoss.All},
                {StandardValueType.Time, StandardValueConversionLoss.All},
                {StandardValueType.ListListString, StandardValueConversionLoss.All},
                {StandardValueType.ListTag, StandardValueConversionLoss.All}
            };

        protected override LinkValue? ConvertToTypedValue(object? currentValue)
        {
            var ld = currentValue as LinkValue;
            return string.IsNullOrEmpty(ld?.Text) && string.IsNullOrEmpty(ld?.Url) ? null : ld;
        }

        protected override string? BuildDisplayValue(LinkValue value)
        {
            return value.ToString();
        }

        public override (string? NewValue, StandardValueConversionLoss? Loss) ConvertToString(LinkValue currentValue)
        {
            if (!string.IsNullOrEmpty(currentValue.Url))
            {
                return (currentValue.Url,
                    string.IsNullOrEmpty(currentValue.Text) ? null : StandardValueConversionLoss.TextWillBeLost);
            }

            return (currentValue.Text, null);
        }

        public override (List<string>? NewValue, StandardValueConversionLoss? Loss) ConvertToListString(
            LinkValue currentValue)
        {
            var (nv, loss) = ConvertToString(currentValue);
            return (nv == null ? null : [nv], loss);
        }

        public override (decimal? NewValue, StandardValueConversionLoss? Loss) ConvertToNumber(LinkValue currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (bool? NewValue, StandardValueConversionLoss? Loss) ConvertToBoolean(LinkValue currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (LinkValue? NewValue, StandardValueConversionLoss? Loss) ConvertToLink(LinkValue currentValue) =>
            (currentValue, null);

        public override async Task<(DateTime? NewValue, StandardValueConversionLoss? Loss)> ConvertToDateTime(
            LinkValue currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (TimeSpan? NewValue, StandardValueConversionLoss? Loss) ConvertToTime(LinkValue currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }
        public override (List<List<string>>? NewValue, StandardValueConversionLoss? Loss) ConvertToMultilevel(
            LinkValue currentValue)
        {
            var (nv, loss) = ConvertToString(currentValue);
            return (nv == null ? null : [[nv]], loss);
        }

        public override (List<TagValue>? NewValue, StandardValueConversionLoss? Loss) ConvertToListTag(LinkValue currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }
    }
}