using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Services;

/// <summary>The exact, currently available files of a result, including workflow placement.</summary>
public sealed record DownloadResultContents(string Directory, IReadOnlyList<string> Files)
{
    public static DownloadResultContents? Resolve(DownloadResultDbModel result, DownloadResultProcessingDbModel? state)
    {
        if (state?.ContentsReadyAt != null && Read(state.ContentsDirectory, state.ContentsFilesJson) is { } placed)
            return placed;
        return result.Kind == DownloadResultKind.LocalFiles ? Read(result.Path, result.FilesJson) : null;
    }

    public static DownloadResultContents? Read(string? directory, string? filesJson)
    {
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(filesJson)) return null;
        try
        {
            if (!Path.IsPathFullyQualified(directory) || !System.IO.Directory.Exists(directory)) return null;
            var root = Path.GetFullPath(directory);
            var files = JsonSerializer.Deserialize<string[]>(filesJson);
            if (files is not { Length: > 0 }) return null;
            foreach (var file in files)
            {
                if (string.IsNullOrWhiteSpace(file) || !Path.IsPathFullyQualified(file) || !File.Exists(file)) return null;
                var relative = Path.GetRelativePath(root, file);
                if (Path.IsPathRooted(relative) || relative == ".." ||
                    relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return null;
            }
            return new(root, files.Select(Path.GetFullPath).Distinct().ToArray());
        }
        catch (Exception error) when (error is JsonException or ArgumentException or NotSupportedException or PathTooLongException)
        { return null; }
    }
}
