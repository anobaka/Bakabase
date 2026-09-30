using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;
using Bakabase.InsideWorld.Models.Constants;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Services;

/// <summary>Locates recorded output without guessing gallery names or scanning a shared download root.</summary>
public sealed class DownloadTaskOutputLocator(BakabaseDbContext db, ExHentaiDownloadLedger ledger)
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".avif", ".bmp", ".tif", ".tiff", ".jxl" };

    public async Task<DownloadTaskOpenTarget?> GetAsync(int taskId, CancellationToken ct = default)
    {
        var task = await db.DownloadTasks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == taskId, ct);
        if (task == null) return null;
        // Other sources retain their existing configured-directory behavior.
        if (task.ThirdPartyId != ThirdPartyId.ExHentai)
            return string.IsNullOrWhiteSpace(task.DownloadPath) ? null : new(task.DownloadPath, false);

        var results = await db.DownloadResults.AsNoTracking()
            .Where(x => x.DownloadTaskId == taskId && x.ThirdPartyId == ThirdPartyId.ExHentai)
            .OrderByDescending(x => x.Id).ToListAsync(ct);
        var latest = results.FirstOrDefault();
        var imagePaths = new HashSet<string>(PathComparer);
        var currentRecordedPaths = new HashSet<string>(PathComparer);
        var supersededPaths = new HashSet<string>(PathComparer);
        // A changed template or an original-image upgrade can create another result for the
        // same gallery. Use its latest files rather than every historical result's paths.
        foreach (var gallery in results.GroupBy(x => x.SourceKey))
        {
            var result = gallery.First();
            if (result.Kind != DownloadResultKind.LocalFiles) continue;
            foreach (var path in ReadPaths(result.FilesJson))
                if (AbsolutePath(path) is { } file && IsWithin(file, result.DownloadDirectory))
                {
                    currentRecordedPaths.Add(file);
                    if (File.Exists(file)) imagePaths.Add(file);
                }
            foreach (var previous in gallery.Skip(1).Where(x => x.Kind == DownloadResultKind.LocalFiles))
                foreach (var path in ReadPaths(previous.FilesJson))
                    if (AbsolutePath(path) is { } file && IsWithin(file, previous.DownloadDirectory))
                        supersededPaths.Add(file);
        }
        // Accounting and old page URLs in the ledger intentionally retain historical paths.
        // They must not widen the latest template's folder. A different gallery may still
        // explicitly own the same path, and unrecorded partial images remain eligible below.
        supersededPaths.ExceptWith(currentRecordedPaths);

        var taskFiles = await db.DownloadTaskFiles.AsNoTracking().Where(x => x.DownloadTaskId == taskId)
            .Select(x => x.Path).ToListAsync(ct);
        foreach (var path in taskFiles)
            if (ExistingFile(path) is { } file && !supersededPaths.Contains(file) &&
                ImageExtensions.Contains(System.IO.Path.GetExtension(file)))
                imagePaths.Add(file);
        try
        {
            // The ledger is written before the accounting callback, so it also covers a
            // cancelled/failed batch that left images but never produced a complete result.
            foreach (var path in await ledger.GetImagePathsAsync(taskId, ct))
                if (ExistingFile(path) is { } file && !supersededPaths.Contains(file)) imagePaths.Add(file);
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            // A damaged ledger still blocks paid downloads. Folder navigation can safely
            // fall back to the independently persisted results and task-owned file list.
        }

        var imageDirectory = CommonDirectory(imagePaths.Select(System.IO.Path.GetDirectoryName));
        if (imageDirectory != null && (!PrefersTorrent(task.Options) || latest?.Kind != DownloadResultKind.TorrentMetadata))
            return new(imageDirectory, false);

        if (latest?.Kind == DownloadResultKind.TorrentMetadata)
        {
            var torrent = ExistingFile(ExHentaiDownloadResultHelper.GetTorrentDownloadPath(latest));
            if (torrent != null) return new(torrent, true);
            // Never select the managed metadata cache when the user output is gone.
            if (ExistingDirectory(latest.DownloadDirectory) is { } torrentDirectory)
                return new(torrentDirectory, false);
        }

        if (imageDirectory != null) return new(imageDirectory, false);
        var recordedDirectory = CommonDirectory(results.Select(x => ExistingDirectory(x.DownloadDirectory)));
        if (recordedDirectory != null) return new(recordedDirectory, false);
        return ExistingDirectory(task.DownloadPath) is { } configuredDirectory
            ? new(configuredDirectory, false) : null;
    }

    private static bool PrefersTorrent(string? options)
    {
        if (string.IsNullOrWhiteSpace(options)) return true;
        try
        {
            return JsonSerializer.Deserialize<ExHentaiTaskOptions>(options, JsonSerializerOptions.Web)?.PreferTorrent != false;
        }
        catch (JsonException) { return true; }
    }

    private static IEnumerable<string?> ReadPaths(string json)
    {
        try { return JsonSerializer.Deserialize<string?[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static string? AbsolutePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathFullyQualified(path)) return null;
        try { return System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path)); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        { return null; }
    }

    private static string? ExistingFile(string? path) => AbsolutePath(path) is { } full && File.Exists(full) ? full : null;
    private static string? ExistingDirectory(string? path) => AbsolutePath(path) is { } full && Directory.Exists(full) ? full : null;

    private static bool IsWithin(string path, string? directory)
    {
        var root = AbsolutePath(directory);
        if (root == null) return false;
        if (string.Equals(path, root, PathComparison)) return true;
        var relative = System.IO.Path.GetRelativePath(root, path);
        return !System.IO.Path.IsPathRooted(relative) && relative != ".." &&
               !relative.StartsWith(".." + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string? CommonDirectory(IEnumerable<string?> directories)
    {
        string? common = null;
        foreach (var directory in directories)
        {
            if (AbsolutePath(directory) is not { } full) continue;
            if (common == null) common = full;
            while (!IsWithin(full, common))
            {
                common = System.IO.Path.GetDirectoryName(common);
                if (common == null) return null;
            }
        }
        return common;
    }
}
