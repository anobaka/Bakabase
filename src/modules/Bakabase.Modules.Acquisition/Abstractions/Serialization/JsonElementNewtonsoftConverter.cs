using System.Text.Json;
using Newtonsoft.Json.Linq;

namespace Bakabase.Modules.Acquisition.Abstractions.Serialization;

/// <summary>Exposes a System.Text.Json value unchanged through the Newtonsoft HTTP boundary.</summary>
public sealed class JsonElementNewtonsoftConverter : Newtonsoft.Json.JsonConverter
{
    public override bool CanConvert(Type objectType) =>
        objectType == typeof(JsonElement) || objectType == typeof(JsonElement?);

    public override void WriteJson(Newtonsoft.Json.JsonWriter writer, object? value,
        Newtonsoft.Json.JsonSerializer serializer)
    {
        if (value is not JsonElement element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            writer.WriteNull();
            return;
        }
        // Raw JSON keeps nested values, string spelling and number precision intact;
        // reflecting JsonElement would instead serialize its ValueKind property.
        writer.WriteRawValue(element.GetRawText());
    }

    public override object? ReadJson(Newtonsoft.Json.JsonReader reader, Type objectType, object? existingValue,
        Newtonsoft.Json.JsonSerializer serializer)
    {
        if (reader.TokenType == Newtonsoft.Json.JsonToken.Null && objectType == typeof(JsonElement?)) return null;
        var priorDateHandling = reader.DateParseHandling;
        try
        {
            // Evidence strings can look like dates; they must remain strings.
            reader.DateParseHandling = Newtonsoft.Json.DateParseHandling.None;
            var token = JToken.Load(reader);
            using var document = JsonDocument.Parse(token.ToString(Newtonsoft.Json.Formatting.None));
            return document.RootElement.Clone();
        }
        finally {reader.DateParseHandling = priorDateHandling;}
    }
}
