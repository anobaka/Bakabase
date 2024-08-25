using Bakabase.Abstractions.Components.Configuration;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Extensions;

namespace Bakabase.Abstractions.Extensions;

public static class StringExtensions
{

    private static readonly char[] PathSeparatorCandidates =
    [
        Path.AltDirectorySeparatorChar,
        Path.DirectorySeparatorChar
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
        var idx = 0;
        while (idx <= str.Length)
        {
            var nextIdx = idx;
            while (true)
            {
                nextIdx = str.IndexOf(separator, nextIdx);
                if (nextIdx > 0)
                {
                    if (str[nextIdx - 1] == escapeChar)
                    {
                        nextIdx++;
                        continue;
                    }
                }

                break;
            }

            if (nextIdx == -1)
            {
                result.Add(str[idx..]);
                break;
            }

            result.Add(str.Substring(idx, nextIdx - idx));
            idx = nextIdx + 1;
        }

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
        return string.Join(separator, data.Select(d => d?.Replace(separator.ToString(), $"{escapeChar}{separator}")));
    }

    public static string? StandardizePath(this string? path)
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
}