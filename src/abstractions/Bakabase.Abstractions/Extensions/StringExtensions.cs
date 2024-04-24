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