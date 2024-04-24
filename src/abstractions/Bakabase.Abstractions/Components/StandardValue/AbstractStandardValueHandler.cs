using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Extensions;

namespace Bakabase.Abstractions.Components.StandardValue
{
    public abstract class AbstractStandardValueHandler<TValue> : IStandardValueHandler
    {
        public abstract StandardValueType Type { get; }

        public abstract Dictionary<StandardValueType, StandardValueConversionLoss?> DefaultConversionLoss { get; }

        public async Task<(object? NewValue, StandardValueConversionLoss? Loss)> Convert(object? currentValue,
            StandardValueType toType)
        {
            var typedCurrentValue = ConvertToTypedValue(currentValue);
            // this is a hardcode, to resolve the mixed using of nullable type and nullable language feature.
            // for example, if TValue is a non-nullable value, the overrides of ConvertToTypedValue must return a non-nullable value, this may hide the original null value of currentValue.
            if (currentValue == null || typedCurrentValue == null)
            {
                return (toType != StandardValueType.Boolean ? false : null, null);
            }

            return toType switch
            {
                StandardValueType.Boolean => ConvertToBoolean(typedCurrentValue),
                StandardValueType.Link => ConvertToLink(typedCurrentValue),
                StandardValueType.DateTime => await ConvertToDateTime(typedCurrentValue),
                StandardValueType.Time => ConvertToTime(typedCurrentValue),
                StandardValueType.String => ConvertToString(typedCurrentValue),
                StandardValueType.ListString => ConvertToListString(typedCurrentValue),
                StandardValueType.Decimal => ConvertToNumber(typedCurrentValue),
                StandardValueType.ListListString => ConvertToMultilevel(typedCurrentValue),
                _ => throw new ArgumentOutOfRangeException(nameof(toType), toType, null)
            };
        }

        public bool ValidateType(object? value)
        {
            return value is TValue;
        }

        public Type ExpectedType => SpecificTypeUtils<TValue>.Type;

        protected abstract TValue? ConvertToTypedValue(object? currentValue);

        public abstract (string? NewValue, StandardValueConversionLoss? Loss) ConvertToString(TValue currentValue);

        public abstract (List<string>? NewValue, StandardValueConversionLoss? Loss) ConvertToListString(
            TValue currentValue);

        public abstract (decimal? NewValue, StandardValueConversionLoss? Loss) ConvertToNumber(TValue currentValue);
        public abstract (bool? NewValue, StandardValueConversionLoss? Loss) ConvertToBoolean(TValue currentValue);
        public abstract (LinkData? NewValue, StandardValueConversionLoss? Loss) ConvertToLink(TValue currentValue);

        public abstract Task<(DateTime? NewValue, StandardValueConversionLoss? Loss)> ConvertToDateTime(
            TValue currentValue);

        public abstract (TimeSpan? NewValue, StandardValueConversionLoss? Loss) ConvertToTime(TValue currentValue);
        public abstract (string? NewValue, StandardValueConversionLoss? Loss) ConvertToFormula(TValue currentValue);

        public abstract (List<List<string>>? NewValue, StandardValueConversionLoss? Loss) ConvertToMultilevel(
            TValue currentValue);
    }
}