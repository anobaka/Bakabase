using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Business.Components.FileExplorer;

namespace Bakabase.Service.Components.Federation;

public sealed record ManagedResourceChildrenPage(IReadOnlyList<IwFsEntry> Entries, string? NextCursor, bool HasMore);

public static class ManagedLibraryResourceFiles
{
    private static readonly IComparer<string> EntryNameComparer = Comparer<string>.Create((left, right) =>
    {
        var comparison = StringComparer.OrdinalIgnoreCase.Compare(left, right);
        return comparison == 0 ? StringComparer.Ordinal.Compare(left, right) : comparison;
    });

    /// <summary>Keep at most one page in memory, even when the directory contains millions of files.</summary>
    public static ManagedResourceChildrenPage ReadChildren(Resource resource, string path, bool showHiddenFiles,
        string? cursor, int pageSize, Func<string, bool> pathAllowed, CancellationToken ct)
    {
        if (pageSize is < 1 or > ManagedLibraryOperations.MaximumPageSize || cursor?.Length > 4096 ||
            cursor?.IndexOfAny(['/', '\\', '\0']) >= 0)
            throw new ArgumentException("Choose a page of at most 100 files.");
        if (!IsWithin(resource, path) || !pathAllowed(path))
            throw new UnauthorizedAccessException("Choose a location belonging to this resource.");
        var isDirectory = Directory.Exists(path);
        var entries = isDirectory ? Directory.EnumerateFileSystemEntries(path) : [path];
        var names = new SortedSet<string>(EntryNameComparer);
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(entry);
            if (!string.IsNullOrEmpty(cursor) && EntryNameComparer.Compare(name, cursor) <= 0) continue;
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0 ||
                !showHiddenFiles && (name.StartsWith('.') || (attributes & FileAttributes.Hidden) != 0) ||
                !pathAllowed(entry)) continue;
            names.Add(name);
            if (names.Count > pageSize + 1) names.Remove(names.Max!);
        }
        var hasMore = names.Count > pageSize;
        var page = names.Take(pageSize).Select(name => new IwFsEntry(isDirectory ? Path.Combine(path, name) : path)).ToArray();
        return new(page, hasMore ? page[^1].Name : null, hasMore);
    }

    public static bool IsAttached(Resource resource, string key) => resource.Properties?.Values
        .SelectMany(pool => pool.Values).Where(property => property.Type == PropertyType.Attachment)
        .SelectMany(property => property.Values ?? []).Any(value =>
            value.Value is IEnumerable<string> paths && paths.Contains(key, StringComparer.Ordinal)) == true;

    public static bool CanReadAsset(Resource resource, string key) => IsAttached(resource, key) ||
        (IsWithin(resource, key) && File.Exists(key));

    /// <summary>Resource details may browse inside this resource, but may not use it to browse another root.</summary>
    public static bool IsWithin(Resource resource, string? path)
    {
        if (!resource.HasLocalPath || string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(resource.Path!));
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (string.Equals(root, target, comparison)) return File.Exists(root) || Directory.Exists(root);
        if (!Directory.Exists(root)) return false;
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!target.StartsWith(prefix, comparison)) return false;
        for (var current = target; !string.Equals(current, root, comparison); current = Path.GetDirectoryName(current)!)
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
        return true;
    }
}
