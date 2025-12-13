using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bootstrap.Extensions;

namespace Bakabase.Modules.StandardValue.Components.ValueHandlers;

public class ListTagValueHandler : AbstractStandardValueHandler<List<TagValue>>
{
    public override StandardValueType Type => StandardValueType.ListTag;

    protected override string? BuildDisplayValue(List<TagValue> value) => string.Join(InternalOptions.TextSeparator,
        value.RemoveEmpty().Select(v => v.ToString()));

    protected override bool ConvertToOptimizedTypedValue(object? currentValue, out List<TagValue>? optimizedTypedValue)
    {
        if (currentValue is List<TagValue> t)
        {
            t = t.RemoveEmpty();
            if (t.Any())
            {
                optimizedTypedValue = t;
                return true;
            }
        }

        optimizedTypedValue = default;
        return false;
    }

    public override string? ConvertToString(List<TagValue> optimizedValue) =>
        string.Join(StandardValueSystem.CommonListItemSeparator, optimizedValue.Select(t => t.ToString()));

    public override List<string>? ConvertToListString(List<TagValue> optimizedValue) =>
        optimizedValue.Select(t => t.ToString()).ToList();

    public override decimal? ConvertToNumber(List<TagValue> optimizedValue) =>
        optimizedValue.FirstNotNullOrDefault(x => x.Name.ConvertToDecimal());

    public override bool? ConvertToBoolean(List<TagValue> optimizedValue) =>
        optimizedValue.FirstNotNullOrDefault(x => x.Name.ConvertToBoolean());

    public override LinkValue? ConvertToLink(List<TagValue> optimizedValue)
    {
        if (optimizedValue.Count == 1)
        {
            var firstTag = optimizedValue[0];
            if (firstTag.Group.IsNullOrEmpty())
            {
                var link = firstTag.Name.ConvertToLinkValue();
                if (link != null)
                {
                    return link;
                }
            }
        }

        return new LinkValue(ConvertToString(optimizedValue), null);
    }


    public override List<List<string>>? ConvertToListListString(List<TagValue> optimizedValue) => optimizedValue
        .Select(t => new List<string?> {t.Group, t.Name}.OfType<string>().ToList()).ToList();

    public override List<TagValue>? ConvertToListTag(List<TagValue> optimizedValue) => optimizedValue;

    protected override List<string>? ExtractTextsForConvertingToDateTimeInternal(List<TagValue> optimizedValue) =>
        optimizedValue.Select(t => t.Name).ToList();

    protected override List<string>? ExtractTextsForConvertingToTime(List<TagValue> optimizedValue) =>
        optimizedValue.Select(t => t.Name).ToList();

    protected override bool CompareInternal(List<TagValue> a, List<TagValue> b) => a.SequenceEqual(b, TagValue.GroupNameComparer);

    /// <summary>
    /// Aggregates multiple tag lists into one by union (using Group+Name as key).
    /// </summary>
    public override object? Combine(IEnumerable<object?> values)
    {
        var seen = new HashSet<(string? Group, string Name)>();
        var result = new List<TagValue>();
        foreach (var value in values)
        {
            if (ConvertToOptimizedTypedValue(value, out var optimized) && optimized != null)
            {
                foreach (var tag in optimized)
                {
                    if (seen.Add((tag.Group, tag.Name)))
                    {
                        result.Add(tag);
                    }
                }
            }
        }
        return result.Count > 0 ? result : null;
    }
}