using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bakabase.Modules.Workflow.Abstractions.Models;

/// <summary>The HTTP host uses Newtonsoft while private workflow snapshots use System.Text.Json.</summary>
public sealed class WorkflowJsonElementConverter : Newtonsoft.Json.JsonConverter
{
    public override bool CanConvert(Type objectType) => objectType == typeof(JsonElement) || objectType == typeof(JsonElement?);

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, Newtonsoft.Json.JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null && objectType == typeof(JsonElement?)) return null;
        // Do not turn strings into dates, or parameter string types would silently change on HTTP input.
        var priorDateHandling = reader.DateParseHandling;
        reader.DateParseHandling = DateParseHandling.None;
        try
        {
            var token = JToken.Load(reader);
            using var document = JsonDocument.Parse(token.ToString(Formatting.None));
            return document.RootElement.Clone();
        }
        finally { reader.DateParseHandling = priorDateHandling; }
    }

    public override void WriteJson(JsonWriter writer, object? value, Newtonsoft.Json.JsonSerializer serializer)
    {
        if (value is not JsonElement element || element.ValueKind == JsonValueKind.Undefined) writer.WriteNull();
        else writer.WriteRawValue(element.GetRawText());
    }
}
