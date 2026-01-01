using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Models.Domain;
using Newtonsoft.Json;

namespace Bakabase.Modules.StandardValue.Components.ValueHandlers
{
    public class ListListStringValueHandler : AbstractStandardValueHandler<List<List<string>>>
    {
        public override StandardValueType Type => StandardValueType.ListListString;

        protected override string? BuildDisplayValue(List<List<string>> value)
        {
            value = value.RemoveEmpty();
            return string.Join(InternalOptions.TextSeparator,
                value.Select(s => string.Join(InternalOptions.LayerTextSeparator, s)));
        }

        protected override bool ConvertToOptimizedTypedValue(object? currentValue,
            out List<List<string>>? optimizedTypedValue)
        {
            if (currentValue is List<List<string>> d)
            {
                d = d.Select(x => x.TrimAndRemoveEmpty()).OfType<List<string>>().ToList();
                if (d.Any())
                {
                    optimizedTypedValue = d;
                    return true;
                }
            }

            optimizedTypedValue = default;
            return false;
        }

        public override string? ConvertToString(List<List<string>> optimizedValue) =>
            string.Join(StandardValueSystem.CommonListItemSeparator,
                optimizedValue.Select(x => string.Join(StandardValueSystem.ListListStringInnerSeparator, x)));

        public override List<string>? ConvertToListString(List<List<string>> optimizedValue) => optimizedValue
            .Select(x => string.Join(StandardValueSystem.ListListStringInnerSeparator, x)).ToList();

        public override decimal? ConvertToNumber(List<List<string>> optimizedValue) =>
            optimizedValue.FirstNotNullOrDefault(x => x.FirstNotNullOrDefault(a => a.ConvertToDecimal()));

        public override bool? ConvertToBoolean(List<List<string>> optimizedValue) =>
            optimizedValue.FirstNotNullOrDefault(x => x.FirstNotNullOrDefault(a => a.ConvertToBoolean()));


        public override LinkValue? ConvertToLink(List<List<string>> optimizedValue) => optimizedValue is [{Count: 1}]
            ? optimizedValue[0][0].ConvertToLinkValue()
            : new LinkValue(ConvertToString(optimizedValue), null);

        public override List<List<string>>? ConvertToListListString(List<List<string>> optimizedValue) =>
            optimizedValue;

        public override List<TagValue>? ConvertToListTag(List<List<string>> optimizedValue)
        {
            return optimizedValue.Select(x =>
                x.Count == 1
                    ? new TagValue(null, x[0])
                    : new TagValue(x[0], string.Join(InternalOptions.LayerTextSeparator, x.Skip(1)))).ToList();
        }

        protected override List<string>?
            ExtractTextsForConvertingToDateTimeInternal(List<List<string>> optimizedValue) =>
            optimizedValue.SelectMany(o => o).ToList();

        protected override List<string>? ExtractTextsForConvertingToTime(List<List<string>> optimizedValue) =>
            optimizedValue.SelectMany(o => o).ToList();

        protected override bool CompareInternal(List<List<string>> a, List<List<string>> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            for (var i = 0; i < a.Count; i++)
            {
                if (!a[i].SequenceEqual(b[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Aggregates multiple list values into one by union.
        /// Uses the serialized inner list as key for deduplication.
        /// </summary>
        public override object? Combine(IEnumerable<object?> values)
        {
            var seen = new HashSet<string>();
            var result = new List<List<string>>();
            foreach (var value in values)
            {
                if (ConvertToOptimizedTypedValue(value, out var optimized) && optimized != null)
                {
                    foreach (var innerList in optimized)
                    {
                        var key = string.Join(StandardValueSystem.ListListStringInnerSeparator, innerList);
                        if (seen.Add(key))
                        {
                            result.Add(innerList);
                        }
                    }
                }
            }
            return result.Count > 0 ? result : null;
        }
    }
}