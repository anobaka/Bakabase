using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Configurations;
using Bakabase.Modules.StandardValue.Abstractions.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bootstrap.Extensions;

namespace Bakabase.Modules.StandardValue.Components.ValueHandlers
{
    public abstract class AbstractStandardValueHandler<TValue>(ICustomDateTimeParser customDateTimeParser)
        : IStandardValueHandler
    {
        public abstract StandardValueType Type { get; }

        private Dictionary<StandardValueType, StandardValueConversionRule>? _possibleConversionLosses;

        public Dictionary<StandardValueType, StandardValueConversionRule> ConversionRules =>
            (_possibleConversionLosses ??= StandardValueOptions.ConversionRules[Type]);


        public async Task<object?> Convert(object? currentValue,
            StandardValueType toType)
        {
            if (currentValue == null)
            {
                return null;
            }

            if (!ConvertToOptimizedTypedValue(currentValue, out var typedCurrentValue))
            {
                return null;
            }

            if (typedCurrentValue == null)
            {
                return null;
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
                StandardValueType.ListListString => ConvertToListListString(typedCurrentValue),
                StandardValueType.ListTag => ConvertToListTag(typedCurrentValue),
                _ => throw new ArgumentOutOfRangeException(nameof(toType), toType, null)
            };
        }

        public bool ValidateType(object? value)
        {
            return value is TValue;
        }

        public Type ExpectedType => SpecificTypeUtils<TValue>.Type;

        public string? BuildDisplayValue(object? value) => value == null ? null : BuildDisplayValue((TValue) value);
        protected virtual string? BuildDisplayValue(TValue value) => value?.ToString();

        /// <summary>
        /// <para>
        /// Why not using <see cref="optimizedTypedValue"/> as return value: due to the mixed usings of nullable-value-type and nullable-reference-type, we need a solution to:
        /// 1. Make sure null or bad <see cref="currentValue"/> will be converted to null for nullable-value-type.
        /// 2. Make ConvertToXXX methods receive a not-null value for better coding experience for implementations.
        /// </para>
        /// </summary>
        /// <param name="currentValue"></param>
        /// <param name="optimizedTypedValue"></param>
        /// <returns></returns>
        protected abstract bool ConvertToOptimizedTypedValue(object? currentValue, out TValue? optimizedTypedValue);

        public abstract string? ConvertToString(TValue optimizedValue);

        public abstract List<string>? ConvertToListString(TValue optimizedValue);

        public abstract decimal? ConvertToNumber(TValue optimizedValue);
        public abstract bool? ConvertToBoolean(TValue optimizedValue);
        public abstract LinkValue? ConvertToLink(TValue optimizedValue);

        protected virtual List<string>? ExtractTextsForConvertingToDateTime(TValue optimizedValue) => null;

        public virtual async Task<DateTime?> ConvertToDateTime(TValue optimizedValue)
        {
            var texts = ExtractTextsForConvertingToDateTime(optimizedValue)?.TrimAndRemoveEmpty();
            if (texts?.Any() == true)
            {
                foreach (var text in texts)
                {
                    var date = await customDateTimeParser.TryToParseDateTime(text);
                    if (date.HasValue)
                    {
                        return date;
                    }

                    date = text.ConvertToDateTime();
                    if (date.HasValue)
                    {
                        return date;
                    }
                }
            }

            return null;
        }

        protected virtual List<string>? ExtractTextsForConvertingToTime(TValue optimizedValue) => null;

        public virtual TimeSpan? ConvertToTime(TValue optimizedValue)
        {
            var texts = ExtractTextsForConvertingToTime(optimizedValue)?.TrimAndRemoveEmpty();
            return texts.FirstNotNullOrDefault(t => t.ConvertToTime());
        }

        public abstract List<List<string>>? ConvertToListListString(TValue optimizedValue);
        public abstract List<TagValue>? ConvertToListTag(TValue optimizedValue);
    }
}