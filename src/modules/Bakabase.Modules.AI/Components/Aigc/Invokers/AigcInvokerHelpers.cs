using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bakabase.Modules.AI.Components.Aigc.Invokers;

/// <summary>
/// Small helpers shared across AIGC invokers.
/// </summary>
internal static class AigcInvokerHelpers
{
    /// <summary>
    /// JSON parse options that tolerate `//` comments in user-supplied config payloads.
    /// The frontend may also strip them before save, but backend tolerance keeps things robust.
    /// </summary>
    public static readonly JsonDocumentOptions LenientJsonDocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static JsonNode? ParseLenient(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonNode.Parse(json, documentOptions: LenientJsonDocumentOptions); }
        catch { return null; }
    }

    public static T? GetParam<T>(this IReadOnlyDictionary<string, object?> parameters, string key, T? fallback = default)
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null) return fallback;

        if (raw is T direct) return direct;

        try
        {
            if (raw is JsonElement je)
            {
                return je.Deserialize<T>();
            }

            return (T?)Convert.ChangeType(raw, typeof(T));
        }
        catch
        {
            return fallback;
        }
    }

    public static string ResolveTemplate(string? template, IReadOnlyDictionary<string, string?> tokens)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;
        var result = template;
        foreach (var (k, v) in tokens)
        {
            result = result.Replace("{" + k + "}", v ?? string.Empty);
        }
        return result;
    }

    /// <summary>
    /// Resolve a simple JSONPath-like expression against a JsonNode.
    /// Supports: $.a.b, $.a[0], $.a[*].b. Returns flattened list of leaf nodes.
    /// </summary>
    public static List<JsonNode?> SelectNodes(JsonNode? root, string path)
    {
        var results = new List<JsonNode?> { root };
        if (string.IsNullOrEmpty(path)) return results;

        var trimmed = path.StartsWith("$.") ? path[2..]
            : path.StartsWith("$") ? path[1..]
            : path;

        // Tokenize on '.' but treat '[...]' as part of the previous segment
        var i = 0;
        while (i < trimmed.Length)
        {
            // Read property name up to '.' or '['
            var start = i;
            while (i < trimmed.Length && trimmed[i] != '.' && trimmed[i] != '[') i++;
            var name = trimmed[start..i];
            if (!string.IsNullOrEmpty(name))
            {
                results = results.Select(n => n is JsonObject jo && jo.TryGetPropertyValue(name, out var v) ? v : null).ToList();
            }

            // Handle bracket index segments
            while (i < trimmed.Length && trimmed[i] == '[')
            {
                var endIdx = trimmed.IndexOf(']', i);
                if (endIdx < 0) break;
                var idx = trimmed[(i + 1)..endIdx];
                if (idx == "*")
                {
                    results = results.SelectMany(n => n is JsonArray arr ? arr.AsEnumerable() : []).ToList();
                }
                else if (int.TryParse(idx, out var iv))
                {
                    results = results.Select(n => n is JsonArray arr && iv < arr.Count ? arr[iv] : null).ToList();
                }
                i = endIdx + 1;
            }

            if (i < trimmed.Length && trimmed[i] == '.') i++;
        }

        return results;
    }

    public static byte[]? DecodeBase64Loose(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        // Tolerate "data:image/png;base64,..." prefix
        var commaIdx = value.IndexOf(',');
        if (value.StartsWith("data:") && commaIdx > 0) value = value[(commaIdx + 1)..];
        try { return Convert.FromBase64String(value); } catch { return null; }
    }

    public static string GuessExtensionFromMime(string? mime, string fallback)
    {
        if (string.IsNullOrEmpty(mime)) return fallback;
        return mime.ToLowerInvariant() switch
        {
            "image/png" => "png",
            "image/jpeg" or "image/jpg" => "jpg",
            "image/webp" => "webp",
            "image/gif" => "gif",
            "video/mp4" => "mp4",
            "video/webm" => "webm",
            "audio/mpeg" => "mp3",
            "audio/wav" or "audio/x-wav" => "wav",
            "text/plain" => "txt",
            "application/json" => "json",
            _ => fallback
        };
    }
}
