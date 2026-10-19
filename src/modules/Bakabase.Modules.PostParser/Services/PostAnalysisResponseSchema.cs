using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bakabase.Modules.PostParser.Services;

/// <summary>Response shape for local structured generation; semantic validation still follows parsing.</summary>
internal static class PostAnalysisResponseSchema
{
    internal static readonly JsonElement Download = CreateDownloadSchema();

    internal static readonly JsonElement Availability = JsonSerializer.SerializeToElement(Object(
        ("status", Enum("noExpiryReported", "expired", "restored", "unknown")),
        ("evidence", Array(String())), ("reason", NullableString())));

    private static JsonElement CreateDownloadSchema()
    {
        var step = Object(
            ("id", String()), ("op", Enum("renameExtension", "renameFile", "moveFile", "extractArchive")),
            ("input", String()), ("selector", NullableString()), ("extension", NullableString()),
            ("password", NullableString()), ("targetName", NullableString()), ("targetDirectory", NullableString()));
        var plan = Object(("requirement", Enum("required", "notRequired", "unknown")),
            ("steps", Array(step)), ("evidence", Array(String())));
        var resource = Object(("link", String()), ("code", NullableString()), ("password", NullableString()),
            ("groupId", NullableString()), ("extraction", plan));
        var group = Object(("id", String()), ("title", String()),
            ("kind", Enum("main", "preview", "supplement", "related", "tool", "unknown")),
            ("summary", NullableString()), ("evidence", Array(String())));
        return JsonSerializer.SerializeToElement(Object(
            ("title", NullableString()), ("resources", Array(resource)), ("groups", Array(group))));
    }

    private static JsonObject Object(params (string Name, JsonNode Schema)[] fields) => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(fields.Select(field => new KeyValuePair<string, JsonNode?>(field.Name, field.Schema))),
        ["required"] = new JsonArray(fields.Select(field => (JsonNode?)JsonValue.Create(field.Name)).ToArray()),
        ["additionalProperties"] = false
    };

    private static JsonObject String() => new() { ["type"] = "string" };
    private static JsonObject NullableString() => new() { ["type"] = new JsonArray("string", "null") };
    private static JsonObject Array(JsonNode items) => new() { ["type"] = "array", ["items"] = items };
    private static JsonObject Enum(params string[] values) => new()
    {
        ["type"] = "string", ["enum"] = new JsonArray(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray())
    };
}
