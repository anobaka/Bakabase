using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bakabase.Modules.DataSync.Canonical;

/// <summary>
/// Canonical JSON (v3.1 §3.1): RFC 8785 (JCS) restricted to what the content DTOs use. UTF-8 without BOM and
/// without insignificant whitespace; object members sorted by key comparing UTF-16 code units ordinally; arrays
/// in their order; integers only (a non-integral number throws: for content a codec produced, that is a codec
/// bug); minimal string escaping, never Utf8JsonWriter's.
/// </summary>
/// <remarks>
/// Local content is not validated, so strings may hold U+0000 or unpaired surrogates. Both are written as
/// escapes (<c>\u0000</c>, <c>\udxxx</c>, lowercase hex) instead of reaching the UTF-8 encoder, which would
/// silently turn a lone surrogate into U+FFFD and make two different local strings hash equal.
/// </remarks>
public static class CanonicalJson
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>The canonical text of <paramref name="node"/>; a null node is <c>null</c>.</summary>
    public static string Serialize(JsonNode? node)
    {
        var sb = new StringBuilder();
        WriteNode(sb, node);
        return sb.ToString();
    }

    /// <summary>The canonical UTF-8 bytes of <paramref name="node"/>; the only input to hashing.</summary>
    public static byte[] SerializeToUtf8Bytes(JsonNode? node) => StrictUtf8.GetBytes(Serialize(node));

    private static void WriteNode(StringBuilder sb, JsonNode? node)
    {
        switch (node)
        {
            case null:
                sb.Append("null");
                break;
            case JsonObject obj:
                WriteObject(sb, obj);
                break;
            case JsonArray array:
                sb.Append('[');
                for (var i = 0; i < array.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    WriteNode(sb, array[i]);
                }

                sb.Append(']');
                break;
            case JsonValue value:
                WriteValue(sb, value);
                break;
            default:
                throw new InvalidOperationException($"Unsupported JSON node {node.GetType().Name}.");
        }
    }

    private static void WriteObject(StringBuilder sb, JsonObject obj)
    {
        var members = obj.ToList();
        members.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        sb.Append('{');
        for (var i = 0; i < members.Count; i++)
        {
            if (i > 0) sb.Append(',');
            WriteString(sb, members[i].Key);
            sb.Append(':');
            WriteNode(sb, members[i].Value);
        }

        sb.Append('}');
    }

    private static void WriteValue(StringBuilder sb, JsonValue value)
    {
        switch (value.GetValueKind())
        {
            case JsonValueKind.String:
                if (value.TryGetValue(out string? text)) WriteString(sb, text);
                else if (value.TryGetValue(out char c)) WriteString(sb, c.ToString());
                else
                    throw new InvalidOperationException(
                        $"Canonical JSON writes strings only; got a {DescribeClrValue(value)} value.");
                break;
            case JsonValueKind.Number:
                if (!JsonNumbers.TryGetInt64(value, out var number))
                    throw new InvalidOperationException(
                        $"Canonical JSON writes integers in long range only; got {value.ToJsonString()}.");
                sb.Append(number.ToString(CultureInfo.InvariantCulture));
                break;
            case JsonValueKind.True:
                sb.Append("true");
                break;
            case JsonValueKind.False:
                sb.Append("false");
                break;
            case JsonValueKind.Null:
                sb.Append("null");
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported JSON value kind {value.GetValueKind()} ({DescribeClrValue(value)}).");
        }
    }

    private static void WriteString(StringBuilder sb, string value)
    {
        sb.Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case < ' ':
                    AppendEscape(sb, c);
                    break;
                default:
                    if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                    {
                        sb.Append(c).Append(value[i + 1]);
                        i++;
                    }
                    else if (char.IsSurrogate(c))
                    {
                        // An unpaired surrogate (local content only): escaped, never replaced.
                        AppendEscape(sb, c);
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        sb.Append('"');
    }

    private static void AppendEscape(StringBuilder sb, char c) =>
        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));

    private static string DescribeClrValue(JsonValue value) =>
        value.TryGetValue(out JsonElement _) ? "parsed" : value.GetType().Name;
}
