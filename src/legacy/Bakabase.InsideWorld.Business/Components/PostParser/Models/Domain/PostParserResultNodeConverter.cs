using System;
using System.Text.Json.Nodes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;

/// <summary>
/// MVC uses Newtonsoft while the workflow and SignalR use System.Text.Json. Serialize the
/// result's JSON value, rather than reflecting over JsonNode's implementation properties.
/// Applied only to this result dictionary; SignalR keeps its native JsonNode handling.
/// </summary>
public sealed class PostParserResultNodeConverter : JsonConverter<JsonNode>
{
    public override void WriteJson(JsonWriter writer, JsonNode? value, JsonSerializer serializer)
    {
        if (value == null) writer.WriteNull();
        else writer.WriteRawValue(value.ToJsonString());
    }

    public override JsonNode? ReadJson(JsonReader reader, Type objectType, JsonNode? existingValue,
        bool hasExistingValue, JsonSerializer serializer) => reader.TokenType == JsonToken.Null
        ? null
        : JsonNode.Parse(JToken.Load(reader).ToString(Formatting.None));
}
