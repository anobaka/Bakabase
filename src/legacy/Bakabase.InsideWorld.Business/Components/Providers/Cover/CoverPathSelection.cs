using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bakabase.Abstractions.Components.Configuration;

namespace Bakabase.InsideWorld.Business.Components.Providers.Cover;

internal static class CoverPathSelection
{
    public static List<string> Available(IEnumerable<string>? paths) =>
        paths?.Where(IsAvailable).ToList() ?? [];

    public static List<string>? FirstAvailable(IEnumerable<IEnumerable<string>?> pathsByPriority) =>
        pathsByPriority.Select(Available).FirstOrDefault(paths => paths.Count > 0);

    private static bool IsAvailable(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        // Only validate local paths here; deciding whether a remote URL is usable belongs
        // to its provider and must not issue network requests while loading a resource list.
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)) return true;

        if (File.Exists(path)) return true;

        // /tool/thumbnail also accepts archive.zip!folder/cover.jpg. Checking the
        // container preserves these paths without opening archives on every list read.
        foreach (var extension in InternalOptions.CompressedFileExtensions)
        {
            var index = path.IndexOf($"{extension}{InternalOptions.CompressedFileRootSeparator}",
                StringComparison.OrdinalIgnoreCase);
            if (index > 0 && File.Exists(path[..(index + extension.Length)])) return true;
        }

        return false;
    }
}
