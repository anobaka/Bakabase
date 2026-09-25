using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Bakabase.Modules.DataSync.Canonical;

/// <summary>
/// System.Text.Json options for data sync's own records (stored JSON columns and records that are not canonical
/// content): camelCase members, enums as their names, nulls left out, depth ≤ 64. Dictionary keys (actor ids,
/// kind ids, child ids) are written as they are. Content that is hashed goes through <see cref="CanonicalJson"/>,
/// never through these options.
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
        options.MakeReadOnly();
        return options;
    }
}
