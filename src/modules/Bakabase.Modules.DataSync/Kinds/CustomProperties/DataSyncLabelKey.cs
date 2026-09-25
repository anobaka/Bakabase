using System.Text;

namespace Bakabase.Modules.DataSync.Kinds.CustomProperties;

/// <summary>
/// The fold key of a label (§3.4): two labels are equal under a property's comparer exactly when their fold keys are
/// ordinal-equal. With ignoreCase off that comparer is <see cref="StringComparer.Ordinal"/> and the key is the label
/// itself; with ignoreCase on it is <see cref="StringComparer.OrdinalIgnoreCase"/> (the Property module's
/// <c>GetLabelComparer</c>), and the key maps every UTF-16 code unit, and every surrogate pair, through the invariant
/// simple upper-casing that comparer uses.
/// </summary>
/// <remarks>
/// <see cref="string.ToUpperInvariant"/> on whole strings is deliberately not used: it maps a few characters that
/// <c>OrdinalIgnoreCase</c> keeps apart (U+017F LATIN SMALL LETTER LONG S becomes 'S'). The per-code-unit table is
/// built once from <see cref="char.ToUpperInvariant"/>, keeping only mappings <c>OrdinalIgnoreCase</c> agrees with,
/// and then closed over every pair <c>OrdinalIgnoreCase</c> treats as equal, so the key agrees with the comparer on
/// every platform's casing data. <c>LabelKeyCrossCheckTests</c> pins the agreement.
/// </remarks>
public static class DataSyncLabelKey
{
    private static readonly Lazy<char[]> BmpTable = new(BuildBmpTable, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The fold key of <paramref name="label"/> under a property whose IgnoreCase is <paramref name="ignoreCase"/>.</summary>
    public static string Fold(string label, bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(label);
        if (!ignoreCase || label.Length == 0) return label;
        var table = BmpTable.Value;
        StringBuilder? sb = null;
        for (var i = 0; i < label.Length; i++)
        {
            var c = label[i];
            if (char.IsHighSurrogate(c) && i + 1 < label.Length && char.IsLowSurrogate(label[i + 1]))
            {
                var rune = new Rune(c, label[i + 1]);
                var folded = FoldRune(rune);
                if (folded != rune)
                {
                    sb ??= new StringBuilder(label, 0, i, label.Length);
                    sb.Append(folded.ToString());
                }
                else
                {
                    sb?.Append(c).Append(label[i + 1]);
                }

                i++;
                continue;
            }

            // A lone surrogate is compared as itself, as the comparer does.
            var mapped = char.IsSurrogate(c) ? c : table[c];
            if (mapped != c) sb ??= new StringBuilder(label, 0, i, label.Length);
            sb?.Append(mapped);
        }

        return sb?.ToString() ?? label;
    }

    /// <summary>The comparer the key agrees with (the Property module's <c>GetLabelComparer</c>).</summary>
    public static StringComparer ComparerFor(bool ignoreCase) =>
        ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static Rune FoldRune(Rune rune)
    {
        var upper = Rune.ToUpperInvariant(rune);
        if (upper == rune) return rune;
        Span<char> a = stackalloc char[2];
        Span<char> b = stackalloc char[2];
        var aLength = rune.EncodeToUtf16(a);
        var bLength = upper.EncodeToUtf16(b);
        return MemoryExtensions.Equals(a[..aLength], b[..bLength], StringComparison.OrdinalIgnoreCase) ? upper : rune;
    }

    private static char[] BuildBmpTable()
    {
        const int size = 0x10000;
        var table = new char[size];
        Span<char> a = stackalloc char[1];
        Span<char> b = stackalloc char[1];

        // 1. The invariant upper-casing, kept only where OrdinalIgnoreCase agrees (U+017F → 'S' is not kept).
        for (var i = 0; i < size; i++)
        {
            var c = (char)i;
            table[i] = c;
            if (char.IsSurrogate(c)) continue;
            var upper = char.ToUpperInvariant(c);
            if (upper == c || char.IsSurrogate(upper)) continue;
            a[0] = c;
            b[0] = upper;
            if (MemoryExtensions.Equals(a, b, StringComparison.OrdinalIgnoreCase)) table[i] = upper;
        }

        // 2. Close over every pair the comparer treats as equal (grouped by its hash code), so that no platform's
        //    casing data can leave two equal code units with different keys. Classes are joined by union-find and
        //    each class takes the smallest key its members already had.
        var parent = new int[size];
        for (var i = 0; i < size; i++) parent[i] = i;
        for (var i = 0; i < size; i++)
        {
            if (!char.IsSurrogate((char)i)) Union(parent, i, table[i]);
        }

        var buckets = new Dictionary<int, List<int>>();
        for (var i = 0; i < size; i++)
        {
            if (char.IsSurrogate((char)i)) continue;
            a[0] = (char)i;
            var hash = string.GetHashCode(a, StringComparison.OrdinalIgnoreCase);
            if (!buckets.TryGetValue(hash, out var bucket)) buckets[hash] = bucket = [];
            bucket.Add(i);
        }

        foreach (var bucket in buckets.Values)
        {
            if (bucket.Count < 2) continue;
            for (var x = 0; x < bucket.Count; x++)
            {
                for (var y = x + 1; y < bucket.Count; y++)
                {
                    a[0] = (char)bucket[x];
                    b[0] = (char)bucket[y];
                    if (MemoryExtensions.Equals(a, b, StringComparison.OrdinalIgnoreCase)) Union(parent, bucket[x], bucket[y]);
                }
            }
        }

        var classKey = new Dictionary<int, char>();
        for (var i = 0; i < size; i++)
        {
            if (char.IsSurrogate((char)i)) continue;
            var root = Find(parent, i);
            if (!classKey.TryGetValue(root, out var key) || table[i] < key) classKey[root] = table[i];
        }

        for (var i = 0; i < size; i++)
        {
            if (!char.IsSurrogate((char)i)) table[i] = classKey[Find(parent, i)];
        }

        return table;
    }

    private static int Find(int[] parent, int i)
    {
        while (parent[i] != i)
        {
            parent[i] = parent[parent[i]];
            i = parent[i];
        }

        return i;
    }

    private static void Union(int[] parent, int x, int y)
    {
        var rx = Find(parent, x);
        var ry = Find(parent, y);
        if (rx != ry) parent[Math.Max(rx, ry)] = Math.Min(rx, ry);
    }
}
