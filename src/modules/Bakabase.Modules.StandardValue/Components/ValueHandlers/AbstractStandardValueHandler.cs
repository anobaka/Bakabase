using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bootstrap.Extensions;

namespace Bakabase.Modules.StandardValue.Components.ValueHandlers
{
    public abstract class AbstractStandardValueHandler<TValue> : IStandardValueHandler
    {
        public abstract StandardValueType Type { get; }

        private Dictionary<StandardValueType, StandardValueConversionRule>? _possibleConversionLosses;

        public Dictionary<StandardValueType, StandardValueConversionRule> ConversionRules =>
            (_possibleConversionLosses ??= new Dictionary<StandardValueType, StandardValueConversionRule>(
                StandardValueSystem.GetAllConversionRules()[Type]));


        public object? Convert(object? currentValue,
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
                StandardValueType.DateTime => ConvertToDateTime(typedCurrentValue),
                StandardValueType.Time => ConvertToTime(typedCurrentValue),
                StandardValueType.String => ConvertToString(typedCurrentValue),
                StandardValueType.ListString => ConvertToListString(typedCurrentValue),
                StandardValueType.Decimal => ConvertToNumber(typedCurrentValue),
                StandardValueType.ListListString => ConvertToListListString(typedCurrentValue),
                StandardValueType.ListTag => ConvertToListTag(typedCurrentValue),
                _ => throw new ArgumentOutOfRangeException(nameof(toType), toType, null)
            };
        }

        public object? Optimize(object? value) => ConvertToOptimizedTypedValue(value, out var x) ? x : null;

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

        protected virtual List<string>? ExtractTextsForConvertingToDateTimeInternal(TValue optimizedValue) => null;

        public virtual DateTime? ConvertToDateTime(TValue optimizedValue)
        {
            var texts = ExtractTextsForConvertingToDateTimeInternal(optimizedValue)?.TrimAndRemoveEmpty();
            if (texts?.Any() == true)
            {
                foreach (var text in texts)
                {
                    // var date = await customDateTimeParser.TryToParseDateTime(text);
                    // if (date.HasValue)
                    // {
                    //     return date;
                    // }

                    var date = text.ConvertToDateTime();
                    if (date.HasValue)
                    {
                        return date;
                    }
                }
            }

            return null;
        }

        public List<string>? ExtractTextsForConvertingToDateTime(object optimizedValue) => optimizedValue is TValue tv
            ? ExtractTextsForConvertingToDateTimeInternal(tv)
            : null;

        public bool Compare(object? a, object? b)
        {
            if (a == null && b == null)
            {
                return true;
            }

            if (a == null && b != null || a != null && b == null)
            {
                return false;
            }

            if (a is not TValue ta || b is not TValue tb)
            {
                return false;
            }

            return CompareInternal(ta, tb);
        }

        protected virtual bool CompareInternal(TValue a, TValue b) => a!.Equals(b);

        protected virtual List<string>? ExtractTextsForConvertingToTime(TValue optimizedValue) => null;

        public virtual TimeSpan? ConvertToTime(TValue optimizedValue)
        {
            var texts = ExtractTextsForConvertingToTime(optimizedValue)?.TrimAndRemoveEmpty();
            return texts.FirstNotNullOrDefault(t => t.ConvertToTime());
        }

        public abstract List<List<string>>? ConvertToListListString(TValue optimizedValue);
        public abstract List<TagValue>? ConvertToListTag(TValue optimizedValue);

        /// <summary>
        /// Default implementation: returns the first non-null optimized value.
        /// Collection-type handlers should override this to aggregate (union) values.
        /// </summary>
        public virtual object? Combine(IEnumerable<object?> values)
        {
            foreach (var value in values)
            {
                if (ConvertToOptimizedTypedValue(value, out var optimized) && optimized != null)
                {
                    return optimized;
                }
            }
            return null;
        }
    }
}