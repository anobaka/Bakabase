using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bootstrap.Extensions;
using Newtonsoft.Json;
using SQLitePCL;

namespace Bakabase.Modules.StandardValue.Extensions;

public static class StandardValueExtensions
{
    private const char SerializationLowLevelSeparator = ',';
    private const char SerializationHighLevelSeparator = ';';
    private const char SerializationSeparatorEscapeChar = '\\';

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
                    return data == null ? null : new LinkValue() {Text = data[0], Url = data[1]};
                }
                case StandardValueType.DateTime:
                    return DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(serializedValue)).DateTime;
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
                // case StandardValueType.String:
                //     return JsonConvert.DeserializeObject<string>(serializedValue);
                // case StandardValueType.ListString:
                //     return JsonConvert.DeserializeObject<List<string>>(serializedValue);
                // case StandardValueType.Decimal:
                //     return JsonConvert.DeserializeObject<decimal>(serializedValue);
                // case StandardValueType.Boolean:
                //     return JsonConvert.DeserializeObject<bool>(serializedValue);
                // case StandardValueType.Link:
                //     return JsonConvert.DeserializeObject<LinkValue>(serializedValue);
                // case StandardValueType.DateTime:
                //     return JsonConvert.DeserializeObject<DateTime>(serializedValue);
                // case StandardValueType.Time:
                //     return JsonConvert.DeserializeObject<TimeSpan>(serializedValue);
                // case StandardValueType.ListListString:
                //     return JsonConvert.DeserializeObject<List<List<string>>>(serializedValue);
                // case StandardValueType.ListTag:
                //     return JsonConvert.DeserializeObject<List<TagValue>>(serializedValue);
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
                    return rawValue is DateTime dt ? dt.ToMillisecondTimestamp().ToString() : null;
                }
                case StandardValueType.Time:
                {
                    return rawValue is TimeSpan ts ? ((int)ts.TotalMilliseconds).ToString() : null;
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
}