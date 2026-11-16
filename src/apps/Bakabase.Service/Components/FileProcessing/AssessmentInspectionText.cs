using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text;
using System.IO;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>Task-local diagnostic text. File names, IDs and proposed values remain visible;
/// authentication credentials and URL tokens are never copied into task metadata.</summary>
internal static class AssessmentInspectionText
{
    internal const int PreviewLimit = 8192;
    internal const int ReadLimit = PreviewLimit * 3;
    internal const int DocumentByteLimit = 2 * 1024 * 1024;
    internal sealed record Document(string Text, bool Truncated, bool Redacted);
    private static readonly Regex Secrets = new("""
        (?ix)
        https?://[^\s<>"']+ |
        (?:bearer|basic)\s+[a-z0-9+/_.=-]+ |
        (?:[a-z0-9_-]*(?:password|passwd|passcode|token|secret|api[_-]?key|authorization|cookie|credential|extract(?:ion)?[_-]?code|access[_-]?code)|pwd|密码|口令|提取码|验证码)
           ["']?\s*(?:[:=：]|is\b|是)\s*(?:"(?:\\.|[^"\\])*"?|'[^']*'?|[^,\r\n}]+) |
        \b(?:sk-[a-z0-9_-]{10,}|eyJ[a-z0-9_-]+\.[a-z0-9_-]+\.[a-z0-9_-]+)
        """, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));

    private static readonly Regex JsonKeys = new("""
        "(?:\\.|[^"\\])*"(?=\s*:)
        """, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly JsonSerializerOptions Json = new() {Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
    private static readonly Regex SecretKey = new("""
        (?ix)^(?:[a-z0-9_-]*(?:password|passwd|passcode|token|secret|api[_-]?key|authorization|cookie|credential|extract(?:ion)?[_-]?code|access[_-]?code)|pwd|密码|口令|提取码|验证码)$
        """, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));

    // Redact the complete document before splitting it into pages. Redacting independent
    // pages could expose a credential whose key and value straddle a page boundary.
    internal static Document Sanitize(string text, int byteLimit = DocumentByteLimit)
    {
        var bounded = BoundUtf8(text, byteLimit);
        try
        {
            var redacted = false;
            var safe = Fragments(bounded.Text, ref redacted, 0);
            safe = new string(safe.Where(c => !char.IsControl(c) || c is '\n' or '\r' or '\t').ToArray());
            var result = BoundUtf8(safe, byteLimit);
            return new(result.Text, bounded.Truncated || result.Truncated, redacted);
        }
        catch (RegexMatchTimeoutException) { return new("[…脱敏超时…]", true, true); }
    }

    private static string Fragments(string text, ref bool redacted, int depth)
    {
        if (depth > 12) { redacted = true; return "[…]"; }
        var result = new StringBuilder();
        var bytes = Encoding.UTF8.GetBytes(text);
        var start = 0;
        var tries = 0;
        for (var i = 0; i < bytes.Length && tries < 512; i++)
        {
            if (bytes[i] != (byte)'{' && bytes[i] != (byte)'[') continue;
            tries++;
            // Parse a JSON value, not an entire line: prompts interleave labelled JSON
            // sections with prose. This preserves context after a password property.
            try
            {
                var reader = new Utf8JsonReader(bytes.AsSpan(i), new JsonReaderOptions {MaxDepth = 32});
                if (!JsonDocument.TryParseValue(ref reader, out var data)) continue;
                using (data)
                {
                    result.Append(Plain(Encoding.UTF8.GetString(bytes.AsSpan(start, i - start)), ref redacted));
                    using var output = new MemoryStream();
                    using (var writer = new Utf8JsonWriter(output, new() {Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping}))
                        Write(writer, data.RootElement, ref redacted, depth + 1);
                    result.Append(Encoding.UTF8.GetString(output.ToArray()));
                    i += checked((int)reader.BytesConsumed) - 1;
                    start = i + 1;
                }
            }
            catch (JsonException) { /* Malformed model output still needs conservative text redaction. */ }
        }
        result.Append(Plain(Encoding.UTF8.GetString(bytes.AsSpan(start)), ref redacted));
        return result.ToString();
    }

    private static void Write(Utf8JsonWriter writer, JsonElement value, ref bool redacted, int depth)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    // A null password or token means no value was provided; masking it
                    // would invent a hidden credential and change the model's JSON type.
                    if (property.Value.ValueKind != JsonValueKind.Null && SecretKey.IsMatch(property.Name))
                    {
                        writer.WriteStringValue("[…]");
                        redacted = true;
                    }
                    else Write(writer, property.Value, ref redacted, depth);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) Write(writer, item, ref redacted, depth);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(Fragments(value.GetString()!, ref redacted, depth));
                break;
            default: value.WriteTo(writer); break;
        }
    }

    private static string Plain(string text, ref bool redacted)
    {
        text = JsonKeys.Replace(text, match =>
        {
            try { return JsonSerializer.Serialize(JsonSerializer.Deserialize<string>(match.Value), Json); }
            catch (JsonException) { return match.Value; }
        });
        var safe = Secrets.Replace(text, "[…]");
        redacted |= safe != text;
        return safe;
    }

    internal static (string Text, bool Truncated) BoundUtf8(string text, int limit)
    {
        if (Encoding.UTF8.GetByteCount(text) <= limit) return (text, false);
        var length = Math.Min(text.Length, limit);
        while (length > 0 && Encoding.UTF8.GetByteCount(text.AsSpan(0, length)) > limit) length /= 2;
        var lo = length;
        var hi = Math.Min(text.Length, limit);
        while (lo < hi)
        {
            var mid = lo + (hi - lo + 1) / 2;
            if (Encoding.UTF8.GetByteCount(text.AsSpan(0, mid)) <= limit) lo = mid;
            else hi = mid - 1;
        }
        if (lo > 0 && char.IsHighSurrogate(text[lo - 1])) lo--;
        return (text[..lo], true);
    }

    internal static (string? Text, bool Truncated) Preview(string? text)
    {
        if (string.IsNullOrEmpty(text)) return (null, false);
        var document = Sanitize(text);
        return (document.Text[..Math.Min(document.Text.Length, PreviewLimit)], document.Truncated || document.Text.Length > PreviewLimit);
    }
}
