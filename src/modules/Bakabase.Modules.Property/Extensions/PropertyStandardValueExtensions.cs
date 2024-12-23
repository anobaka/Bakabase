using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Extensions;

namespace Bakabase.Modules.Property.Extensions;

public static class PropertyStandardValueExtensions
{
    public static string? SerializeDbValueAsStandardValue(this object? value, PropertyType type) =>
        value?.SerializeAsStandardValue(type.GetDbValueType());

    public static object? DeserializeDbValueAsStandardValue(this string? value, PropertyType type) =>
        value?.DeserializeAsStandardValue(type.GetDbValueType());

    public static TValue? DeserializeDbValueAsStandardValue<TValue>(this string? value, PropertyType type)
    {
        var v = value?.DeserializeAsStandardValue(type.GetDbValueType());
        if (v is TValue tv)
        {
            return tv;
        }

        return default;
    }

    public static string? SerializeBizValueAsStandardValue(this object? value, PropertyType type) =>
        value?.SerializeAsStandardValue(type.GetBizValueType());

    public static object? DeserializeBizValueAsStandardValue(this string? value, PropertyType type) =>
        value?.DeserializeAsStandardValue(type.GetBizValueType());

    public static TValue? DeserializeBizValueAsStandardValue<TValue>(this string? value, PropertyType type)
    {
        var v = value?.DeserializeAsStandardValue(type.GetBizValueType());
        if (v is TValue tv)
        {
            return tv;
        }

        return default;
    }
}