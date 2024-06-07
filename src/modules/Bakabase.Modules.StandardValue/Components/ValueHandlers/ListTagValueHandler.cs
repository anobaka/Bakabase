using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.StandardValue.Components.ValueHandlers;

public class ListTagValueHandler : AbstractStandardValueHandler<List<TagValue>>
{
    public override StandardValueType Type => StandardValueType.ListTag;

    public override Dictionary<StandardValueType, StandardValueConversionLoss?> DefaultConversionLoss { get; } =
        new Dictionary<StandardValueType, StandardValueConversionLoss?>()
        {
            {
                StandardValueType.String,
                StandardValueConversionLoss.ValuesWillBeMerged | StandardValueConversionLoss.TagGroupAndNameWillBeMerged
            },
            {
                StandardValueType.ListString,
                StandardValueConversionLoss.ValuesWillBeMerged | StandardValueConversionLoss.TagGroupAndNameWillBeMerged
            },
            {StandardValueType.Decimal, StandardValueConversionLoss.All},
            {StandardValueType.Link, StandardValueConversionLoss.All},
            {StandardValueType.Boolean, StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue},
            {StandardValueType.DateTime, StandardValueConversionLoss.All},
            {StandardValueType.Time, StandardValueConversionLoss.All},
            {
                StandardValueType.ListListString,
                StandardValueConversionLoss.ValuesWillBeMerged | StandardValueConversionLoss.TagGroupAndNameWillBeMerged
            },
            {StandardValueType.ListTag, null}
        };

    protected override List<TagValue>? ConvertToTypedValue(object? currentValue)
    {
        var tv = currentValue as List<TagValue>;
        return tv?.Any() == true ? tv : null;
    }

    protected override string? BuildDisplayValue(List<TagValue> value) => string.Join(InternalOptions.TextSeparator, value.Select(v => v.ToString()));

    public override (string? NewValue, StandardValueConversionLoss? Loss) ConvertToString(List<TagValue> currentValue)
    {
        var loss = currentValue.Any(c => !string.IsNullOrEmpty(c.Group))
            ? StandardValueConversionLoss.TagGroupAndNameWillBeMerged
            : default;
        var value = string.Join(InternalOptions.TextSeparator, currentValue.Select(c => c.ToString()));
        return (value, currentValue.Count > 1 ? StandardValueConversionLoss.ValuesWillBeMerged | loss : loss);
    }

    public override (List<string>? NewValue, StandardValueConversionLoss? Loss) ConvertToListString(
        List<TagValue> currentValue)
    {
        var loss = currentValue.Any(c => !string.IsNullOrEmpty(c.Group))
            ? StandardValueConversionLoss.TagGroupAndNameWillBeMerged
            : default;
        return (currentValue.Select(c => c.ToString()).ToList(), loss);
    }

    public override (decimal? NewValue, StandardValueConversionLoss? Loss) ConvertToNumber(List<TagValue> currentValue)
    {
        return (null, StandardValueConversionLoss.All);
    }

    public override (bool? NewValue, StandardValueConversionLoss? Loss) ConvertToBoolean(List<TagValue> currentValue)
    {
        return (true, StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue);
    }

    public override (LinkValue? NewValue, StandardValueConversionLoss? Loss) ConvertToLink(List<TagValue> currentValue)
    {
        return (null, StandardValueConversionLoss.All);
    }

    public override async Task<(DateTime? NewValue, StandardValueConversionLoss? Loss)> ConvertToDateTime(
        List<TagValue> currentValue)
    {
        return (null, StandardValueConversionLoss.All);
    }

    public override (TimeSpan? NewValue, StandardValueConversionLoss? Loss) ConvertToTime(List<TagValue> currentValue)
    {
        return (null, StandardValueConversionLoss.All);
    }

    public override (List<List<string>>? NewValue, StandardValueConversionLoss? Loss) ConvertToMultilevel(
        List<TagValue> currentValue)
    {
        return (
            currentValue.Select(v => string.IsNullOrEmpty(v.Group) ? new List<string> {v.Name} : [v.Group, v.Name])
                .ToList(), null);
    }

    public override (List<TagValue>? NewValue, StandardValueConversionLoss? Loss) ConvertToListTag(
        List<TagValue> currentValue)
    {
        return (currentValue, null);
    }
}