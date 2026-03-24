using System.Collections.Frozen;

namespace Bakabase.Abstractions.Models.Domain.Constants;

/// <summary>
/// Static mapping from PropertyType to its StandardValueType pair (DbValueType, BizValueType).
/// This lives in abstractions so that Resource.Property can derive these values from Type.
/// </summary>
public static class PropertyTypeValueTypes
{
    private static readonly FrozenDictionary<PropertyType, (StandardValueType DbValueType, StandardValueType BizValueType)> Map =
        new Dictionary<PropertyType, (StandardValueType, StandardValueType)>
        {
            { PropertyType.SingleLineText, (StandardValueType.String, StandardValueType.String) },
            { PropertyType.MultilineText, (StandardValueType.String, StandardValueType.String) },
            { PropertyType.SingleChoice, (StandardValueType.String, StandardValueType.String) },
            { PropertyType.MultipleChoice, (StandardValueType.ListString, StandardValueType.ListString) },
            { PropertyType.Number, (StandardValueType.Decimal, StandardValueType.Decimal) },
            { PropertyType.Percentage, (StandardValueType.Decimal, StandardValueType.Decimal) },
            { PropertyType.Rating, (StandardValueType.Decimal, StandardValueType.Decimal) },
            { PropertyType.Boolean, (StandardValueType.Boolean, StandardValueType.Boolean) },
            { PropertyType.Link, (StandardValueType.Link, StandardValueType.Link) },
            { PropertyType.Attachment, (StandardValueType.ListString, StandardValueType.ListString) },
            { PropertyType.Date, (StandardValueType.DateTime, StandardValueType.DateTime) },
            { PropertyType.DateTime, (StandardValueType.DateTime, StandardValueType.DateTime) },
            { PropertyType.Time, (StandardValueType.Time, StandardValueType.Time) },
            { PropertyType.Formula, (StandardValueType.String, StandardValueType.String) },
            { PropertyType.Multilevel, (StandardValueType.ListString, StandardValueType.ListListString) },
            { PropertyType.Tags, (StandardValueType.ListString, StandardValueType.ListTag) },
        }.ToFrozenDictionary();

    public static StandardValueType GetDbValueType(PropertyType type) =>
        Map.TryGetValue(type, out var pair) ? pair.DbValueType : default;

    public static StandardValueType GetBizValueType(PropertyType type) =>
        Map.TryGetValue(type, out var pair) ? pair.BizValueType : default;
}
