using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Bakabase.InsideWorld.Business.Components.Compression;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>Shared filesystem boundaries for human deliveries and processing plans.</summary>
public static class FileProcessingFiles
{
    public static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static string Within(string path, string root)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.Equals(fullRoot, comparison) && !full.StartsWith(Path.EndsInDirectorySeparator(fullRoot) ? fullRoot : fullRoot + Path.DirectorySeparatorChar, comparison))
            throw new InvalidOperationException("A selected file is outside the selected directory.");
        var cursor = full;
        while (cursor.Length >= fullRoot.Length)
        {
            if ((File.Exists(cursor) || Directory.Exists(cursor)) &&
                (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Symbolic links cannot be processed as file inputs.");
            if (cursor.Equals(fullRoot, comparison)) break;
            cursor = Path.GetDirectoryName(cursor)!;
        }
        return full;
    }

    public static List<string> Enumerate(string root)
    {
        Within(root, root);
        var files = new List<string>();
        void Walk(string directory)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory).OrderBy(x => x, PathComparer))
            {
                Within(path, root);
                if (Directory.Exists(path)) Walk(path); else files.Add(path);
            }
        }
        Walk(Path.GetFullPath(root));
        return files;
    }

    public static bool IsVolume(string path) => Regex.IsMatch(Path.GetFileName(path),
        @"(?:\.part\d+\.[^.]+|\.[rz]\d{2,}|\.\d{3,})$", RegexOptions.IgnoreCase);

    public static IReadOnlyList<string> ExpandVolumes(IEnumerable<string> selected)
    {
        var result = selected.Select(Path.GetFullPath).ToHashSet(PathComparer);
        foreach (var directory in result.Select(Path.GetDirectoryName).Distinct(PathComparer).ToList())
        {
            var groups = CompressedFileHelper.DetectCompressedFileGroups(Directory.GetFiles(directory!), true);
            foreach (var group in groups.Where(g => g.Files.Any(result.Contains)))
                foreach (var file in group.Files) result.Add(file);
        }
        return result.OrderBy(x => x, PathComparer).ToList();
    }

    /// <summary>Checks detectable gaps. Unknown final volume counts still need a human completion signal.</summary>
    public static void ValidateVolumes(IEnumerable<string> files)
    {
        foreach (var group in CompressedFileHelper.DetectCompressedFileGroups(files.ToArray(), true))
        {
            var numbered = group.Files.Select(f => Regex.Match(Path.GetFileName(f),
                    @"(?:\.part(?<part>\d+)\.[^.]+|\.(?<family>[rz])(?<legacy>\d{2,})|\.(?<number>\d{3,}))$",
                    RegexOptions.IgnoreCase)).Where(m => m.Success).ToList();
            if (numbered.Count == 0) continue;
            var first = group.Files[0];
            var stem = Regex.Replace(Path.GetFileName(first), @"(?:\.part\d+\.[^.]+|\.[rz]\d{2,}|\.\d{3,})$", "", RegexOptions.IgnoreCase);
            var unfinished = new[] {".crdownload", ".part", ".partial", ".download", ".tmp", ".!qb", ".aria2"};
            if (Directory.EnumerateFiles(Path.GetDirectoryName(first)!, stem + "*").Any(f =>
                    unfinished.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)))
                throw new InvalidOperationException("An archive part is still downloading.");
            var indices = numbered.Select(m => int.Parse(m.Groups["part"].Success ? m.Groups["part"].Value :
                m.Groups["legacy"].Success ? m.Groups["legacy"].Value : m.Groups["number"].Value)).Order().ToArray();
            var start = numbered[0].Groups["family"].Value.Equals("r", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            if (!indices.SequenceEqual(Enumerable.Range(start, indices.Length)))
                throw new InvalidOperationException($"The archive volume set for {Path.GetFileName(group.Files[0])} has missing parts.");
            var legacy = numbered[0].Groups["family"].Value;
            if (legacy.Length > 0 && !group.Files.Any(f => Path.GetExtension(f).Equals(
                    legacy.Equals("r", StringComparison.OrdinalIgnoreCase) ? ".rar" : ".zip", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("The first archive file of a volume set is missing.");
        }
    }
}
