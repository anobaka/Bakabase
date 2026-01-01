using System.Collections.Concurrent;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bootstrap.Extensions;
using Newtonsoft.Json;
using SQLitePCL;
using System.Security.Policy;
using Bakabase.Modules.StandardValue.Abstractions.Components;

namespace Bakabase.Modules.StandardValue.Extensions;

public static class StandardValueExtensions
{
    private const char SerializationLowLevelSeparator = ',';
    private const char SerializationHighLevelSeparator = ';';
    private const char SerializationSeparatorEscapeChar = '\\';

    public static IStandardValueHandler GetHandler(this StandardValueType type) =>
        StandardValueSystem.GetHandler(type);

    public static object? DeserializeAsStandardValue(this string serializedValue, StandardValueType valueType,
        bool throwOnError = false)
    {
        try
        {
            switch (valueType)
            {
                case StandardValueType.String:
                    return serializedValue;
                case StandardValueType.ListString:
                    return serializedValue.SplitWithEscapeChar(SerializationLowLevelSeparator,
                        SerializationSeparatorEscapeChar);
                case StandardValueType.Decimal:
                    return decimal.Parse(serializedValue);
                case StandardValueType.Boolean:
                    return bool.Parse(serializedValue);
                case StandardValueType.Link:
                {
                    var data = serializedValue.SplitWithEscapeChar(SerializationLowLevelSeparator,
                        SerializationSeparatorEscapeChar);
                    return data == null ? null : new LinkValue(data[0], data[1]);
                }
                case StandardValueType.DateTime:
                    return DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(serializedValue)).ToLocalTime().DateTime;
                case StandardValueType.Time:
                    return TimeSpan.FromMilliseconds(long.Parse(serializedValue));
                case StandardValueType.ListListString:
                    return serializedValue.SplitWithEscapeChar(SerializationHighLevelSeparator,
                        SerializationLowLevelSeparator, SerializationSeparatorEscapeChar);
                case StandardValueType.ListTag:
                {
                    var data = serializedValue.SplitWithEscapeChar(SerializationHighLevelSeparator,
                        SerializationLowLevelSeparator, SerializationSeparatorEscapeChar);
                    return data?.Select(d => new TagValue(d[0], d[1])).ToList();
                }
            }

            throw new ArgumentOutOfRangeException(nameof(valueType), valueType, null);
        }
        catch (Exception)
        {
            if (throwOnError)
            {
                throw;
            }

            return null;
        }
    }

    public static T? DeserializeAsStandardValue<T>(this string serializedValue, StandardValueType valueType,
        bool throwOnError = false)
    {
        var v = DeserializeAsStandardValue(serializedValue, valueType);
        return v is T v1 ? v1 : default;
    }

    public static string? SerializeAsStandardValue(this object rawValue, StandardValueType valueType,
        bool throwOnError = false)
    {
        try
        {
            // return JsonConvert.SerializeObject(rawValue);
            switch (valueType)
            {
                case StandardValueType.String:
                    return rawValue as string;
                case StandardValueType.ListString:
                {
                    return rawValue is List<string> list
                        ? list.Join(SerializationLowLevelSeparator, SerializationSeparatorEscapeChar)
                        : null;
                }
                case StandardValueType.Decimal:
                    return rawValue.ToString();
                case StandardValueType.Link:
                {
                    return rawValue is LinkValue lv
                        ? new[] {lv.Text, lv.Url}.Join(SerializationLowLevelSeparator, SerializationSeparatorEscapeChar)
                        : null;
                }
                case StandardValueType.Boolean:
                    return rawValue.ToString();
                case StandardValueType.DateTime:
                {
                    return rawValue is DateTime dt ? new DateTimeOffset(dt).ToUnixTimeMilliseconds().ToString() : null;
                }
                case StandardValueType.Time:
                {
                    return rawValue is TimeSpan ts ? ((int) ts.TotalMilliseconds).ToString() : null;
                }
                case StandardValueType.ListListString:
                {
                    return rawValue is List<List<string>> d
                        ? d.Select(t => t.Join(SerializationLowLevelSeparator, SerializationSeparatorEscapeChar))
                            .Join(SerializationHighLevelSeparator, SerializationSeparatorEscapeChar)
                        : null;
                }
                case StandardValueType.ListTag:
                {
                    return rawValue is List<TagValue> ts
                        ? ts.Select(t =>
                            new[] {t.Group, t.Name}.Join(SerializationLowLevelSeparator,
                                SerializationSeparatorEscapeChar)).Join(SerializationHighLevelSeparator,
                            SerializationSeparatorEscapeChar)
                        : null;
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(valueType), valueType, null);
            }
        }
        catch (Exception)
        {
            if (throwOnError)
            {
                throw;
            }

            return null;
        }
    }

    public static bool IsStandardValueType(this object? value, StandardValueType type) => value == null || type switch
    {
        StandardValueType.String => value is string,
        StandardValueType.ListString => value is List<string>,
        StandardValueType.Decimal => value is decimal,
        StandardValueType.Link => value is LinkValue,
        StandardValueType.Boolean => value is bool,
        StandardValueType.DateTime => value is DateTime,
        StandardValueType.Time => value is TimeSpan,
        StandardValueType.ListListString => value is List<List<string>>,
        StandardValueType.ListTag => value is List<TagValue>,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };


    private static readonly ConcurrentDictionary<Type, StandardValueType> TypeStdValueTypeMap =
        new ConcurrentDictionary<Type, StandardValueType>(new Dictionary<Type, StandardValueType>
        {
            {SpecificTypeUtils<string>.Type, StandardValueType.String},
            {SpecificTypeUtils<List<string>>.Type, StandardValueType.ListString},
            {SpecificTypeUtils<decimal>.Type, StandardValueType.Decimal},
            {SpecificTypeUtils<LinkValue>.Type, StandardValueType.Link},
            {SpecificTypeUtils<bool>.Type, StandardValueType.Boolean},
            {SpecificTypeUtils<DateTime>.Type, StandardValueType.DateTime},
            {SpecificTypeUtils<TimeSpan>.Type, StandardValueType.Time},
            {SpecificTypeUtils<List<List<string>>>.Type, StandardValueType.ListListString},
            {SpecificTypeUtils<List<TagValue>>.Type, StandardValueType.ListTag},
        });

    public static StandardValueType? InferStandardValueType(this Type type) =>
        TypeStdValueTypeMap.GetValueOrDefault(type);

    public static StandardValueType? InferStandardValueType(this object? value)
    {
        return value?.GetType().InferStandardValueType();
    }
}