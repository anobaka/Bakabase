using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.StandardValue.Models.Domain;
using Newtonsoft.Json;

namespace Bakabase.Modules.StandardValue.Helpers;

public static class StandardValueHelper
{
    public static object? DeserializeAsStandardValue(this string json, StandardValueType valueType)
    {
        return valueType switch
        {
            StandardValueType.String => JsonConvert.DeserializeObject<string>(json),
            StandardValueType.ListString => JsonConvert.DeserializeObject<List<string>>(json),
            StandardValueType.Decimal => JsonConvert.DeserializeObject<decimal>(json),
            StandardValueType.Boolean => JsonConvert.DeserializeObject<bool>(json),
            StandardValueType.Link => JsonConvert.DeserializeObject<LinkValue>(json),
            StandardValueType.DateTime => JsonConvert.DeserializeObject<DateTime>(json),
            StandardValueType.Time => JsonConvert.DeserializeObject<TimeSpan>(json),
            StandardValueType.ListListString => JsonConvert.DeserializeObject<List<List<string>>>(json),
            StandardValueType.ListTag => JsonConvert.DeserializeObject<List<TagValue>>(json),
            _ => throw new ArgumentOutOfRangeException(nameof(valueType), valueType, null)
        };
    }

    public static string? SerializeAsStandardValue(this object? value)
    {
        return value == null ? null : JsonConvert.SerializeObject(value);
    }
}