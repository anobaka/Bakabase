using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Service.Components.Federation;

public sealed record ManagedBrowserFilesPage(IReadOnlyList<PlayableItem> Items, int Offset, int? NextOffset,
    bool HasMore, bool PlayableFilesConfigured);

/// <summary>Only files belonging to one owner resource; neither listing nor delivery accepts a new root.</summary>
public static class ManagedLibraryBrowserFiles
{
    public const int MaximumPageSize = 100;
    public const int MaximumOffset = 1_000_000;
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static ManagedBrowserFilesPage Read(Resource resource, ResourceProfilePlayableFileOptions? options,
        int offset, int pageSize, CancellationToken ct, bool allFiles = false, string? initialPath = null)
    {
        if (offset < 0 || offset > MaximumOffset || pageSize is < 1 or > MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(offset), "Choose a page of at most 100 files.");
        var configured = options?.Extensions is {Count: > 0};
        if (!resource.HasLocalPath) return new([], offset, null, false, configured);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(resource.Path!));
        // Missing and inaccessible resources are errors, never successful empty collections.
        var attributes = File.GetAttributes(root);
        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        var files = isDirectory ? Directory.EnumerateFiles(root, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint
        }) : new[] {root};
        var matches = allFiles ? (_ => true) : Matcher(options);
        if (!string.IsNullOrEmpty(initialPath))
        {
            if (initialPath.Length > 4096) throw new ArgumentException("Choose a resource file.");
            var matchedIndex = 0;
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (!BrowserFile(file) || !matches(file)) continue;
                if (string.Equals(file.StandardizePath(), initialPath.StandardizePath(), PathComparison))
                { offset = matchedIndex / pageSize * pageSize; break; }
                if (++matchedIndex > MaximumOffset) break;
            }
        }
        var items = new List<PlayableItem>(pageSize);
        var skipped = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            if (!BrowserFile(file) || !matches(file)) continue;
            if (skipped++ < offset) continue;
            if (items.Count == pageSize) return new(items, offset, offset + items.Count, true, configured);
            items.Add(new PlayableItem
                {Origin = DataOrigin.FileSystem, Key = file.StandardizePath()!, DisplayName = Path.GetFileName(file)});
        }
        return new(items, offset, null, false, configured);
    }

    public static bool CanRead(Resource resource, ResourceProfilePlayableFileOptions? options, string key)
    {
        if (!resource.HasLocalPath || string.IsNullOrWhiteSpace(key) || !Path.IsPathFullyQualified(key) ||
            !BrowserFile(key)) return false;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(resource.Path!));
        var path = Path.GetFullPath(key);
        var rootIsDirectory = (File.GetAttributes(root) & FileAttributes.Directory) != 0;
        if (!rootIsDirectory) return string.Equals(root, path, PathComparison) && Matcher(options)(path);
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, PathComparison) || !File.Exists(path) || !Matcher(options)(path)) return false;
        // A file or subdirectory symlink is not a way to escape the owner's selected resource.
        for (var current = path; !string.Equals(current, root, PathComparison); current = Path.GetDirectoryName(current)!)
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
        return true;
    }

    public static bool MayUseAlternateProvider(Resource resource, string key)
    {
        if (!resource.HasLocalPath || key.Contains(InternalOptions.CompressedFileRootSeparator)) return true;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(resource.Path!));
        var path = Path.GetFullPath(key);
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        // A stale cached native candidate must not bypass a newly changed profile, or a
        // child symlink rejected above. Only genuinely separate provider targets qualify.
        return !string.Equals(root, path, PathComparison) && !path.StartsWith(prefix, PathComparison);
    }

    public static bool CanExplicitlyOpenUnconfiguredFile(Resource resource,
        ResourceProfilePlayableFileOptions? options, DataOrigin origin, string key) =>
        origin is DataOrigin.FileSystem or DataOrigin.Manual && options?.Extensions is not {Count: > 0} &&
        CanRead(resource, options, key);

    private static Func<string, bool> Matcher(ResourceProfilePlayableFileOptions? options)
    {
        if (options?.Extensions is not {Count: > 0}) return _ => true;
        var extensions = options.Extensions.Select(e => e.StartsWith('.') ? e : "." + e)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Regex? pattern = null;
        if (!string.IsNullOrEmpty(options.FileNamePattern))
            try { pattern = new Regex(options.FileNamePattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250)); }
            catch (ArgumentException) { /* Match the ordinary provider's invalid-pattern behavior. */ }
        return path => extensions.Contains(Path.GetExtension(path)) &&
                       (pattern == null || pattern.IsMatch(Path.GetFileName(path)));
    }

    public static bool BrowserFile(string path)
    {
        var extension = Path.GetExtension(path);
        return InternalOptions.ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
               InternalOptions.VideoExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
               InternalOptions.AudioExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
               InternalOptions.TextExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
}
