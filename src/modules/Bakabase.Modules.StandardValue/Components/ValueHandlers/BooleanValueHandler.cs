using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.StandardValue.Components.ValueHandlers
{
    public class BooleanValueHandler : AbstractStandardValueHandler<bool>
    {
        public override StandardValueType Type => StandardValueType.Boolean;

        public override Dictionary<StandardValueType, StandardValueConversionLoss?> DefaultConversionLoss { get; } =
            new()
            {
                {StandardValueType.String, null},
                {StandardValueType.ListString, null},
                {StandardValueType.Decimal, null},
                {StandardValueType.Link, StandardValueConversionLoss.All},
                {StandardValueType.Boolean, null},
                {StandardValueType.DateTime, StandardValueConversionLoss.All},
                {StandardValueType.Time, StandardValueConversionLoss.All},
                {StandardValueType.ListListString, null},
                {StandardValueType.ListTag, StandardValueConversionLoss.All}
            };

        protected override string BuildDisplayValue(bool value)
        {
            return (value ? 1 : 0).ToString();
        }

        protected override bool ConvertToTypedValue(object? currentValue) => currentValue is true;

        public override (string? NewValue, StandardValueConversionLoss? Loss) ConvertToString(bool currentValue)
        {
            return (BuildDisplayValue(currentValue), null);
        }

        public override (List<string>? NewValue, StandardValueConversionLoss? Loss) ConvertToListString(
            bool currentValue)
        {
            return ([BuildDisplayValue(currentValue)], null);
        }

        public override (decimal? NewValue, StandardValueConversionLoss? Loss) ConvertToNumber(bool currentValue)
        {
            return (currentValue ? 1 : 0, null);
        }

        public override (bool? NewValue, StandardValueConversionLoss? Loss) ConvertToBoolean(bool currentValue)
        {
            return (currentValue, null);
        }

        public override (LinkValue? NewValue, StandardValueConversionLoss? Loss) ConvertToLink(bool currentValue)
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

        public override (List<List<string>>? NewValue, StandardValueConversionLoss? Loss) ConvertToMultilevel(
            bool currentValue)
        {
            return ([[BuildDisplayValue(currentValue)]], null);
        }

        public override (List<TagValue>? NewValue, StandardValueConversionLoss? Loss) ConvertToListTag(bool currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }
    }
}