using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Bakabase.Service.Components.CloudDownloads;

internal static class CloudDownloadManifest
{
    /// <summary>A successful tool exit is insufficient: only its explicit, existing output files belong to this task.</summary>
    public static void Validate(string outputDirectory, IReadOnlyList<string> files)
    {
        if (!Path.IsPathFullyQualified(outputDirectory)) throw new InvalidDataException("The cloud destination must be an absolute path.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory)) + Path.DirectorySeparatorChar;
        var comparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (files.Count == 0 || files.Distinct(comparer).Count() != files.Count)
            throw new InvalidDataException("The cloud tool returned an empty or duplicate file manifest.");
        foreach (var file in files)
        {
            if (!Path.IsPathFullyQualified(file) || !Path.GetFullPath(file).StartsWith(root, comparison) || !File.Exists(file))
                throw new InvalidDataException("The cloud manifest contains a missing file or a path outside its destination.");
            for (FileSystemInfo? entry = new FileInfo(file); entry != null;
                 entry = entry is FileInfo f ? f.Directory : ((DirectoryInfo)entry).Parent)
                if (entry.LinkTarget != null) throw new InvalidDataException("Symbolic links are not supported in cloud download destinations.");
        }
    }
}
