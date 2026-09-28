using System;
using System.IO;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;

/// <summary>Validates gallery output paths without assigning ownership or writing metadata.</summary>
public static class ExHentaiGalleryOutputPath
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>Creates or reuses a gallery directory beneath an existing download root.</summary>
    public static string Resolve(string downloadRoot, string relativeGalleryDirectory)
    {
        if (string.IsNullOrWhiteSpace(downloadRoot))
            throw new ArgumentException("A download root is required.", nameof(downloadRoot));
        if (string.IsNullOrWhiteSpace(relativeGalleryDirectory) || Path.IsPathRooted(relativeGalleryDirectory))
            throw new ArgumentException("A relative gallery directory is required.", nameof(relativeGalleryDirectory));

        var root = Path.GetFullPath(downloadRoot);
        var directory = Path.GetFullPath(Path.Combine(root, relativeGalleryDirectory));
        if (!IsDescendant(root, directory))
            throw new ArgumentException("The gallery directory must be inside the download root.",
                nameof(relativeGalleryDirectory));

        RequireAccessibleRoot(root);
        EnsureNoLinksBelowRoot(root, directory);
        Directory.CreateDirectory(directory);
        // Another process can replace a path component while the directory is being created.
        EnsureNoLinksBelowRoot(root, directory);
        return directory;
    }

    /// <summary>Rejects an output path that leaves its gallery or traverses a link.</summary>
    public static void EnsureSafeOutputPath(string downloadRoot, string galleryDirectory, string file)
    {
        var root = Path.GetFullPath(downloadRoot);
        var gallery = Path.GetFullPath(galleryDirectory);
        var output = Path.GetFullPath(file);
        if (!IsDescendant(root, gallery) || !IsDescendant(gallery, output))
            throw new IOException($"ExHentai gallery file path escaped its gallery directory: {file}");

        EnsureNotLink(root);
        EnsureNoLinksBelowRoot(root, output);
    }

    private static void RequireAccessibleRoot(string root)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"ExHentai download root is unavailable: {root}");

        EnsureNotLink(root);
        // Directory.Exists alone cannot distinguish an unreadable folder from a readable one.
        using var entries = Directory.EnumerateFileSystemEntries(root).GetEnumerator();
        _ = entries.MoveNext();
    }

    private static void EnsureNoLinksBelowRoot(string root, string path)
    {
        var current = root;
        foreach (var segment in Path.GetRelativePath(root, path).Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            try
            {
                EnsureNotLink(current);
            }
            catch (FileNotFoundException)
            {
                break;
            }
            catch (DirectoryNotFoundException)
            {
                break;
            }
        }
    }

    private static void EnsureNotLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"ExHentai gallery output path contains a link: {path}");
    }

    private static bool IsDescendant(string parent, string child) =>
        child.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                         Path.DirectorySeparatorChar, PathComparison);
}
