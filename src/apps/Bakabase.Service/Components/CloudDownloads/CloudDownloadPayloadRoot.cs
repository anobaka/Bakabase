using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Bakabase.Service.Components.CloudDownloads;

/// <summary>Separates a provider-proven content root from the frozen physical download destination.</summary>
internal static class CloudDownloadPayloadRoot
{
    // The caller still validates the original file manifest. An optional layout improvement
    // must not turn a successful download into a failure, or silently drop an outside sidecar.
    public static string Select(string downloadDirectory, string? candidate, IReadOnlyList<string> files) =>
        TryValidate(downloadDirectory, candidate, files, out var root) ? root : downloadDirectory;

    public static string SelectLayout(string downloadDirectory, string? candidate, IReadOnlyList<string> files,
        CloudDownloadTarget target) => target.SelectedSource?.ProviderId == "baidu" && target.DownloadLayout == "separateDirectory"
            ? downloadDirectory : Select(downloadDirectory, candidate, files);

    public static bool TryValidate(string downloadDirectory, string? candidate, IReadOnlyList<string> files,
        out string root)
    {
        root = downloadDirectory;
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        try
        {
            if (!Path.IsPathFullyQualified(downloadDirectory) || !Path.IsPathFullyQualified(candidate)) return false;
            var physicalRoot = Path.GetFullPath(downloadDirectory);
            var contentRoot = Path.GetFullPath(candidate);
            var relative = Path.GetRelativePath(physicalRoot, contentRoot);
            if (Path.IsPathRooted(relative) || relative == ".." ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                !Directory.Exists(contentRoot)) return false;
            if (files.Any(path => !Path.IsPathFullyQualified(path))) return false;
            var paths = files.Select(Path.GetFullPath).ToArray();
            var comparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            if (paths.Distinct(comparer).Count() != paths.Length) return false;
            // Includes existence, every file's containment, duplicate detection and link checks.
            // The same check is used when resuming persisted results, not only at download time.
            CloudDownloadManifest.Validate(contentRoot, paths);
            root = contentRoot;
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
                                     or NotSupportedException or System.Security.SecurityException)
        { return false; }
    }
}
