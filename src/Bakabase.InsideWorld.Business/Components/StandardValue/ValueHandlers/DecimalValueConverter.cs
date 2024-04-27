using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.StandardValue;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.StandardValue.ValueHandlers
{
    public class DecimalValueConverter : AbstractStandardValueHandler<decimal>
    {
        public override StandardValueType Type => StandardValueType.Decimal;

        public override Dictionary<StandardValueType, StandardValueConversionLoss?> DefaultConversionLoss { get; } =
            new()
            {
                {StandardValueType.String, null},
                {StandardValueType.ListString, null},
                {StandardValueType.Decimal, null},
                {StandardValueType.Link, StandardValueConversionLoss.All},
                {StandardValueType.Boolean, StandardValueConversionLoss.NonZeroValueWillBeConvertedToTrue},
                {StandardValueType.DateTime, StandardValueConversionLoss.All},
                {StandardValueType.Time, StandardValueConversionLoss.All},
                {StandardValueType.ListListString, StandardValueConversionLoss.All},

            };

        public override (bool? NewValue, StandardValueConversionLoss? Loss) ConvertToBoolean(decimal currentValue)
        {
            return (currentValue != 0,
                currentValue == 0 ? null : StandardValueConversionLoss.NonZeroValueWillBeConvertedToTrue);
        }

        public override Task<(DateTime? NewValue, StandardValueConversionLoss? Loss)> ConvertToDateTime(
            decimal currentValue)
        {
            return Task.FromResult<(DateTime? NewValue, StandardValueConversionLoss? Loss)>((null,
                StandardValueConversionLoss.All));
        }

        public override (string? NewValue, StandardValueConversionLoss? Loss) ConvertToFormula(decimal currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (LinkData? NewValue, StandardValueConversionLoss? Loss) ConvertToLink(decimal currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (List<string>? NewValue, StandardValueConversionLoss? Loss) ConvertToListString(
            decimal currentValue)
        {
            return ([currentValue.ToString(CultureInfo.InvariantCulture)], null);
        }

        public override (List<List<string>>? NewValue, StandardValueConversionLoss? Loss) ConvertToMultilevel(
            decimal currentValue)
        {
            return ([[currentValue.ToString(CultureInfo.InvariantCulture)]], null);
        }

        public override (decimal? NewValue, StandardValueConversionLoss? Loss) ConvertToNumber(decimal currentValue)
        {
            return (currentValue, null);
        }

        public override (string? NewValue, StandardValueConversionLoss? Loss) ConvertToString(decimal currentValue)
        {
            return (currentValue.ToString(CultureInfo.InvariantCulture), null);
        }

        public override (TimeSpan? NewValue, StandardValueConversionLoss? Loss) ConvertToTime(decimal currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        protected override decimal ConvertToTypedValue(object? currentValue)
        {
            return currentValue is decimal d ? d : 0;
        }
    }
}