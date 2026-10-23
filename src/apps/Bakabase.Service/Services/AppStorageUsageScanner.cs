using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Service.Models.View;

namespace Bakabase.Service.Services;

/// <summary>One read-only walk, with no file content reads and no traversal of links inside AppData.</summary>
internal static class AppStorageUsageScanner
{
    internal static AppStorageUsageViewModel Scan(string root, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var rootInfo = new DirectoryInfo(Path.GetFullPath(root));
        if (!rootInfo.Exists) throw new DirectoryNotFoundException($"Data directory does not exist: {rootInfo.FullName}");
        var totals = AppDataStorage.CategoryIds.ToDictionary(id => id, _ => new CategoryTotal());
        var errors = new List<AppStorageUsageErrorViewModel>();
        long errorCount = 0;
        long links = 0;
        var pending = new Stack<DirectoryInfo>();
        pending.Push(rootInfo);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false
        };

        void RecordError(string path, Exception error)
        {
            errorCount++;
            var category = AppDataStorage.GetCategoryId(Path.GetRelativePath(rootInfo.FullName, path));
            totals[category].Partial = true;
            // A denied directory can conceal any number of files. Bound response/memory size.
            if (errors.Count < 100) errors.Add(new AppStorageUsageErrorViewModel {Path = path, Message = error.Message});
        }

        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Check again at visit time: a queued directory may have become a symlink.
                directory.Refresh();
                if (!directory.Exists) throw new DirectoryNotFoundException($"Directory disappeared: {directory.FullName}");
                if (directory != rootInfo && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    links++;
                    continue;
                }
                foreach (var entry in directory.EnumerateFileSystemInfos("*", options))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) { links++; continue; }
                        if (entry is DirectoryInfo child) pending.Push(child);
                        else if (entry is FileInfo file)
                        {
                            var category = AppDataStorage.GetCategoryId(Path.GetRelativePath(rootInfo.FullName, file.FullName));
                            var bytes = file.Length;
                            totals[category].Bytes += bytes;
                            totals[category].FileCount++;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        RecordError(entry.FullName, ex);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Root unavailability is a failed scan, not a successful empty library.
                if (directory == rootInfo) throw;
                RecordError(directory.FullName, ex);
            }
        }

        var categories = AppDataStorage.CategoryIds.Select(id => new AppStorageUsageCategoryViewModel
        {
            Id = id,
            Bytes = totals[id].Bytes,
            FileCount = totals[id].FileCount,
            Path = AppDataStorage.GetCategoryPath(rootInfo.FullName, id),
            Partial = totals[id].Partial
        }).ToArray();
        return new AppStorageUsageViewModel
        {
            RootPath = rootInfo.FullName,
            TotalBytes = categories.Sum(c => c.Bytes),
            FileCount = categories.Sum(c => c.FileCount),
            ScannedAt = DateTimeOffset.UtcNow,
            Partial = errorCount > 0,
            SkippedSymbolicLinks = links,
            Categories = categories,
            Errors = errors,
            ErrorCount = errorCount
        };
    }

    private sealed class CategoryTotal
    {
        public long Bytes;
        public long FileCount;
        public bool Partial;
    }
}
