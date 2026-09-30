using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bakabase.Modules.DataSync.Canonical;

/// <summary>Reads a JSON number as an integer, whether the node was parsed or built from a CLR value.</summary>
internal static class JsonNumbers
{
    /// <summary>
    /// True for an integer in <see cref="long"/> range: a parsed number written without fraction or exponent, or
    /// a CLR integer (or an integral decimal). Floating-point values are never integers here.
    /// </summary>
    public static bool TryGetInt64(JsonValue value, out long result)
    {
        result = 0;
        if (value.TryGetValue<JsonElement>(out var element))
            return element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out result);
        if (value.TryGetValue(out long l)) { result = l; return true; }
        if (value.TryGetValue(out int i)) { result = i; return true; }
        if (value.TryGetValue(out short s)) { result = s; return true; }
        if (value.TryGetValue(out sbyte sb)) { result = sb; return true; }
        if (value.TryGetValue(out byte b)) { result = b; return true; }
        if (value.TryGetValue(out ushort us)) { result = us; return true; }
        if (value.TryGetValue(out uint ui)) { result = ui; return true; }
        if (value.TryGetValue(out ulong ul))
        {
            if (ul > long.MaxValue) return false;
            result = (long)ul;
            return true;
        }
        if (value.TryGetValue(out decimal d))
        {
            if (d != decimal.Truncate(d) || d < long.MinValue || d > long.MaxValue) return false;
            result = (long)d;
            return true;
        }
        return false;
    }
}
