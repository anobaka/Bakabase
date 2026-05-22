using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Extensions;

namespace Bakabase.Abstractions.Extensions;

public static partial class NaturalSort
{
    [GeneratedRegex(@"(\d+)")]
    private static partial Regex NumberRegex();

    private static int CompareParts(object[] xParts, object[] yParts)
    {
        var minLength = Math.Min(xParts.Length, yParts.Length);
        for (var i = 0; i < minLength; i++)
        {
            int result;
            var xIsNum = xParts[i] is long;
            var yIsNum = yParts[i] is long;

            if (xIsNum && yIsNum)
            {
                result = ((long)xParts[i]).CompareTo((long)yParts[i]);
            }
            else if (xIsNum)
            {
                result = -1; // numbers before strings
            }
            else if (yIsNum)
            {
                result = 1;
            }
            else
            {
                result = string.Compare((string)xParts[i], (string)yParts[i], StringComparison.OrdinalIgnoreCase);
            }

            if (result != 0) return result;
        }

        return xParts.Length.CompareTo(yParts.Length);
    }

    private static object[] ParseParts(string s)
    {
        var parts = NumberRegex().Split(s);
        var result = new object[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            result[i] = long.TryParse(parts[i], out var num) ? num : parts[i];
        }
        return result;
    }

    public static IOrderedEnumerable<T> OrderByNatural<T>(this IEnumerable<T> source, Func<T, string?> keySelector)
    {
        return source.OrderBy(x =>
        {
            var key = keySelector(x);
            return key == null ? null : ParseParts(key);
        }, PartsComparer.Default);
    }

    public static IOrderedEnumerable<string> OrderByNatural(this IEnumerable<string> source)
    {
        return source.OrderByNatural(x => x);
    }

    private class PartsComparer : IComparer<object[]?>
    {
        public static readonly PartsComparer Default = new();

        public int Compare(object[]? x, object[]? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;
            return CompareParts(x, y);
        }
    }
}

public static class StringExtensions
{

    private static readonly char[] PathSeparatorCandidates =
    [
        Path.AltDirectorySeparatorChar,
        Path.DirectorySeparatorChar,
        InternalOptions.WindowsSpecificDirSeparator
        // Path.VolumeSeparatorChar, 
        // Path.PathSeparator
    ];

    /// <summary>
    /// 
    /// </summary>
    /// <param name="str"></param>
    /// <param name="escapeChar"></param>
    /// <param name="separator"></param>
    /// <returns></returns>
    public static List<string>? SplitWithEscapeChar(this string str, char separator, char escapeChar)
    {
        if (str.Length == 0)
        {
            return null;
        }

        var result = new List<string>();
        var current = new StringBuilder();
        var i = 0;
        while (i < str.Length)
        {
            var c = str[i];
            if (c == escapeChar && i + 1 < str.Length)
            {
                var next = str[i + 1];
                // The new format escapes only the escape char itself and the separator.
                // Legacy data never escaped the escape char, so an escape char before any
                // other character is kept literally for backward compatibility.
                if (next == escapeChar || next == separator)
                {
                    current.Append(next);
                    i += 2;
                    continue;
                }

                current.Append(c);
                i += 1;
                continue;
            }

            if (c == separator)
            {
                result.Add(current.ToString());
                current.Clear();
                i += 1;
                continue;
            }

            current.Append(c);
            i += 1;
        }

        result.Add(current.ToString());
        return result;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="str"></param>
    /// <param name="escapeChar"></param>
    /// <param name="highLevelSeparator"></param>
    /// <param name="lowLevelSeparator"></param>
    /// <returns></returns>
    public static List<List<string>>? SplitWithEscapeChar(this string str, char highLevelSeparator,
        char lowLevelSeparator, char escapeChar)
    {
        var lowLevelStrings = str.SplitWithEscapeChar(separator: highLevelSeparator, escapeChar: escapeChar);
        if (lowLevelStrings == null)
        {
            return null;
        }

        return lowLevelStrings
            .Select(x => x.SplitWithEscapeChar(separator: lowLevelSeparator, escapeChar: escapeChar) ?? [])
            .ToList();
    }

    public static string Join(this IEnumerable<string?> data, char separator, char escapeChar)
    {
        // Escape the escape char first, then the separator, so the escape chars introduced
        // for separators are not themselves re-escaped.
        return string.Join(separator, data.Select(d => d
            ?.Replace($"{escapeChar}", $"{escapeChar}{escapeChar}")
            .Replace($"{separator}", $"{escapeChar}{separator}")));
    }

    public static string? StandardizePath([NotNullIfNotNull(nameof(path))] this string? path)
    {
        if (path.IsNullOrEmpty())
        {
            return null;
        }

        var sp = string.Join(InternalOptions.DirSeparator,
            path!.Split(PathSeparatorCandidates, StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.TrimEnd())
                .Where(a => !string.IsNullOrEmpty(a)));

        // windows drive
        if (sp.EndsWith(':'))
        {
            sp += '/';
        }

        if (path.IsUncPath())
        {
            sp = InternalOptions.UncPathPrefix + sp;
        }

        if (path.StartsWith('/'))
        {
            sp = $"/{sp}";
        }

        return sp;
    }

    public static bool IsUncPath(this string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        if (path.StartsWith(InternalOptions.UncPathPrefix))
        {
            path = $"{InternalOptions.WindowsSpecificUncPathPrefix}{path[2..]}";
        }

        try
        {
            var uri = new Uri(path);
            return uri.IsUnc;
        }
        catch
        {
            return false;
        }
    }

    public static List<string>? TrimAndRemoveEmpty(this IEnumerable<string?> arr)
    {
        var r = arr.Select(a => a?.Trim()).Where(a => a.IsNotEmpty()).OfType<string>().ToList();
        return !r.Any() ? null : r;
    }

    public static void TrimAll(this IList<string> arr)
    {
        for (var i = 0; i < arr.Count; i++)
        {
            arr[i] = arr[i].Trim();
        }
    }

    public static List<List<string>> RemoveEmpty(this IEnumerable<IEnumerable<string>> arr) => arr
        .Select(b => b.Select(x => x.Trim()).Where(x => x.IsNotEmpty()).ToList()).Where(x => x.Any()).ToList();

    public static void TrimAll(this List<List<string>> branches)
    {
        foreach (var t in branches)
        {
            for (var j = 0; j < t.Count; j++)
            {
                t[j] = t[j].Trim();
            }
        }
    }
}