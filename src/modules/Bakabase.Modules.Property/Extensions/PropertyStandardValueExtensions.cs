using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Extensions;

namespace Bakabase.Modules.Property.Extensions;

public static class PropertyStandardValueExtensions
{
    public static string? SerializeDbValueAsStandardValue(this object? value, PropertyType type) =>
        value?.SerializeAsStandardValue(PropertySystem.Property.GetDbValueType(type));

    public static object? DeserializeDbValueAsStandardValue(this string? value, PropertyType type) =>
        value?.DeserializeAsStandardValue(PropertySystem.Property.GetDbValueType(type));

    public static TValue? DeserializeDbValueAsStandardValue<TValue>(this string? value, PropertyType type)
    {
        var v = value?.DeserializeAsStandardValue(PropertySystem.Property.GetDbValueType(type));
        if (v is TValue tv)
        {
            return tv;
        }

        return default;
    }

    public static string? SerializeBizValueAsStandardValue(this object? value, PropertyType type) =>
        value?.SerializeAsStandardValue(PropertySystem.Property.GetBizValueType(type));

    public static object? DeserializeBizValueAsStandardValue(this string? value, PropertyType type) =>
        value?.DeserializeAsStandardValue(PropertySystem.Property.GetBizValueType(type));

    public static TValue? DeserializeBizValueAsStandardValue<TValue>(this string? value, PropertyType type)
    {
        var v = value?.DeserializeAsStandardValue(PropertySystem.Property.GetBizValueType(type));
        if (v is TValue tv)
        {
            return tv;
        }

        return default;
    }
}