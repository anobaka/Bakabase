using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Bakabase.Modules.DataSync.Identity;

namespace Bakabase.Modules.DataSync.Canonical;

/// <summary>
/// System.Text.Json options for data sync's own records (stored JSON columns and records that are not canonical
/// content): camelCase members, enums as their names, nulls left out, depth ≤ 64. Dictionary keys (actor ids,
/// kind ids, child ids) are written as they are. Identity values are written as their plain forms: a
/// <see cref="SyncKey"/> and a <see cref="DataSyncActorId"/> as strings, <see cref="EntityKeys"/> as an array of
/// strings, a version vector as its canonical object. Content that is hashed goes through
/// <see cref="CanonicalJson"/>, never through these options.
/// </summary>
public static class DataSyncJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            MaxDepth = 64,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new SyncKeyJsonConverter());
        options.Converters.Add(new ActorIdJsonConverter());
        options.Converters.Add(new EntityKeysJsonConverter());
        options.MakeReadOnly();
        return options;
    }

    /// <summary>A sync key as its 32-hex string; reading validates it.</summary>
    private sealed class SyncKeyJsonConverter : JsonConverter<SyncKey>
    {
        public override SyncKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            return SyncKey.IsValid(value) ? new SyncKey(value!) : throw new JsonException($"Invalid sync key '{value}'.");
        }

        public override void Write(Utf8JsonWriter writer, SyncKey value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value ?? throw new JsonException("An uninitialized sync key cannot be written."));
    }

    /// <summary>An actor id as its 16-hex string; reading validates it.</summary>
    private sealed class ActorIdJsonConverter : JsonConverter<DataSyncActorId>
    {
        public override DataSyncActorId Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            var value = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            return DataSyncActorId.IsValid(value)
                ? new DataSyncActorId(value!)
                : throw new JsonException($"Invalid actor id '{value}'.");
        }

        public override void Write(Utf8JsonWriter writer, DataSyncActorId value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value ?? throw new JsonException("An uninitialized actor id cannot be written."));
    }

    /// <summary>Every key of an entity as an array of strings, primary first.</summary>
    private sealed class EntityKeysJsonConverter : JsonConverter<EntityKeys>
    {
        public override EntityKeys Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Entity keys are an array.");
            var keys = new List<SyncKey>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                var value = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                if (!SyncKey.IsValid(value)) throw new JsonException($"Invalid sync key '{value}'.");
                keys.Add(new SyncKey(value!));
            }

            return keys.Count == 0 ? EntityKeys.None : new EntityKeys(keys);
        }

        public override void Write(Utf8JsonWriter writer, EntityKeys value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var key in value.All) writer.WriteStringValue(key.Value);
            writer.WriteEndArray();
        }
    }
}
