using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Models.Domain;
using Newtonsoft.Json;

namespace Bakabase.Modules.StandardValue.Extensions;

public static class StandardValueExtensions
{
    public static object? DeserializeAsStandardValue(this string serializedValue, StandardValueType valueType,
        bool throwOnError = false)
    {
        try
        {
            switch (valueType)
            {
                case StandardValueType.String:
                    return JsonConvert.DeserializeObject<string>(serializedValue);
                case StandardValueType.ListString:
                    return JsonConvert.DeserializeObject<List<string>>(serializedValue);
                case StandardValueType.Decimal:
                    return JsonConvert.DeserializeObject<decimal>(serializedValue);
                case StandardValueType.Boolean:
                    return JsonConvert.DeserializeObject<bool>(serializedValue);
                case StandardValueType.Link:
                    return JsonConvert.DeserializeObject<LinkValue>(serializedValue);
                case StandardValueType.DateTime:
                    return JsonConvert.DeserializeObject<DateTime>(serializedValue);
                case StandardValueType.Time:
                    return JsonConvert.DeserializeObject<TimeSpan>(serializedValue);
                case StandardValueType.ListListString:
                    return JsonConvert.DeserializeObject<List<List<string>>>(serializedValue);
                case StandardValueType.ListTag:
                    return JsonConvert.DeserializeObject<List<TagValue>>(serializedValue);
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

    public static string? SerializeAsStandardValue(this object rawValue, bool throwOnError = false)
    {
        try
        {
            return JsonConvert.SerializeObject(rawValue);
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