using System.Collections.Concurrent;
using System.Globalization;
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
                    // New data is written with InvariantCulture. Legacy data may be in the
                    // machine's culture, so fall back to CurrentCulture. NumberStyles.Float
                    // disallows group separators, removing the "1,234" ambiguity.
                    return decimal.TryParse(serializedValue, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out var dec)
                        ? dec
                        : decimal.Parse(serializedValue, NumberStyles.Float, CultureInfo.CurrentCulture);
                case StandardValueType.Boolean:
                {
                    // Historic writers produced "True"/"False" (bool.ToString()), while the
                    // conversion/display layer emits "1"/"0" — accept all of them on read.
                    var trimmed = serializedValue.Trim();
                    if (bool.TryParse(trimmed, out var b))
                    {
                        return b;
                    }

                    return trimmed switch
                    {
                        "1" => true,
                        "0" => false,
                        _ => throw new FormatException($"'{serializedValue}' is not a valid boolean value")
                    };
                }
                case StandardValueType.Link:
                {
                    // Well-formed payloads always carry two segments (text,url); tolerate a
                    // single-segment legacy/corrupted payload as a text-only link instead of
                    // silently dropping the whole value.
                    var data = serializedValue.SplitWithEscapeChar(SerializationLowLevelSeparator,
                        SerializationSeparatorEscapeChar);
                    return data == null || data.Count == 0
                        ? null
                        : new LinkValue(data[0], data.Count > 1 ? data[1] : null);
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
                    // Well-formed entries always carry two segments (group,name); tolerate a
                    // single-segment entry as a group-less tag instead of failing the whole list.
                    var data = serializedValue.SplitWithEscapeChar(SerializationHighLevelSeparator,
                        SerializationLowLevelSeparator, SerializationSeparatorEscapeChar);
                    return data?
                        .Select(d => d.Count switch
                        {
                            0 => null,
                            1 => new TagValue(null, d[0]),
                            _ => new TagValue(d[0], d[1])
                        })
                        .Where(t => !string.IsNullOrEmpty(t?.Name))
                        .OfType<TagValue>()
                        .ToList();
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
        var v = DeserializeAsStandardValue(serializedValue, valueType, throwOnError);
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
                    // Always write with InvariantCulture so the stored format is
                    // culture-independent (and matches the frontend, which parses with `.`).
                    // Non-decimal numerics (int/double/…) are normalized through decimal;
                    // anything non-numeric is a type mismatch and must not be stored.
                    return rawValue switch
                    {
                        decimal dec => dec.ToString(CultureInfo.InvariantCulture),
                        sbyte or byte or short or ushort or int or uint or long or ulong or float or double =>
                            System.Convert.ToDecimal(rawValue, CultureInfo.InvariantCulture)
                                .ToString(CultureInfo.InvariantCulture),
                        _ => null
                    };
                case StandardValueType.Link:
                {
                    return rawValue is LinkValue lv
                        ? new[] {lv.Text, lv.Url}.Join(SerializationLowLevelSeparator, SerializationSeparatorEscapeChar)
                        : null;
                }
                case StandardValueType.Boolean:
                    // Keep bool.ToString()'s "True"/"False" — that's what all stored data uses.
                    return rawValue is bool b ? b.ToString() : null;
                case StandardValueType.DateTime:
                {
                    // ToUniversalTime() first: new DateTimeOffset(dt) throws for
                    // DateTime.Min/MaxValue once a non-UTC local offset is applied.
                    return rawValue is DateTime dt
                        ? new DateTimeOffset(dt.ToUniversalTime()).ToUnixTimeMilliseconds().ToString()
                        : null;
                }
                case StandardValueType.Time:
                {
                    // long, not int: TimeSpan.TotalMilliseconds overflows Int32
                    // for durations beyond ~24.8 days.
                    return rawValue is TimeSpan ts ? ((long) ts.TotalMilliseconds).ToString() : null;
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
        // TryGetValue, not GetValueOrDefault: a miss must be null, not (StandardValueType)0.
        TypeStdValueTypeMap.TryGetValue(type, out var v) ? v : null;

    public static StandardValueType? InferStandardValueType(this object? value)
    {
        return value?.GetType().InferStandardValueType();
    }
}