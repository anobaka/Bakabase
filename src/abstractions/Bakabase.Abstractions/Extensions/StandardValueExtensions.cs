using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Newtonsoft.Json;

namespace Bakabase.Abstractions.Extensions;

public static class StandardValueExtensions
{
    public static object? DeserializeAsStandardValue(this string json, StandardValueType valueType)
    {
        return valueType switch
        {
            StandardValueType.String => JsonConvert.DeserializeObject<string>(json),
            StandardValueType.ListString => JsonConvert.DeserializeObject<List<string>>(json),
            StandardValueType.Decimal => JsonConvert.DeserializeObject<decimal>(json),
            StandardValueType.Boolean => JsonConvert.DeserializeObject<bool>(json),
            StandardValueType.Link => JsonConvert.DeserializeObject<LinkData>(json),
            StandardValueType.DateTime => JsonConvert.DeserializeObject<DateTime>(json),
            StandardValueType.Time => JsonConvert.DeserializeObject<TimeSpan>(json),
            StandardValueType.ListListString => JsonConvert.DeserializeObject<List<List<string>>>(json),
            _ => throw new ArgumentOutOfRangeException(nameof(valueType), valueType, null)
        };
    }
}