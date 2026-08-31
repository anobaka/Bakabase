using System;
using System.IO;
using System.Linq;
using Bakabase.Abstractions.Extensions;

namespace Bakabase.InsideWorld.Business.Components.ResourceMove;

/// <summary>
/// Filesystem helpers for the move pipeline that the Bootstrap primitives get wrong or
/// don't cover: POSIX volume detection and cleanup of a failed directory move's debris.
/// </summary>
public static class ResourceMoveFileSystem
{
    /// <summary>
    /// Whether two paths live on the same filesystem. <see cref="Path.GetPathRoot(string)"/>
    /// is what the Bootstrap move primitives use, but on POSIX every absolute path roots at
    /// "/", so a cross-mount move mis-takes the rename fast path and dies with EXDEV.
    /// Resolve each path to its longest matching mount point instead. Returns null when it
    /// cannot be determined — callers should then fall back to the primitives' own choice.
    /// </summary>
    public static bool? AreOnSameFileSystem(string a, string b)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b),
                    StringComparison.OrdinalIgnoreCase);
            }

            var mounts = DriveInfo.GetDrives()
                .Select(d => d.RootDirectory.FullName.StandardizePath())
                .OfType<string>()
                .Distinct()
                .OrderByDescending(m => m.Length)
                .ToList();

            string? MountOf(string path)
            {
                var full = Path.GetFullPath(path).StandardizePath()!;

                return mounts.FirstOrDefault(m => full.IsPathEqualOrUnder(m));
            }

            var mountA = MountOf(a);
            var mountB = MountOf(b);
            if (mountA == null || mountB == null)
            {
                return null;
            }

            return string.Equals(mountA, mountB, StringComparison.Ordinal);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Best-effort removal of a directory tree that carries no files — the shape a failed
    /// directory move leaves behind at the destination (the primitives create the directory
    /// skeleton before any file lands). Removing it keeps the retry probe's
    /// "destination already exists" check meaningful. A tree containing any file is left
    /// untouched: those files may be the only remaining copy after a partial move.
    /// </summary>
    public static void TryDeleteFilelessDirectoryTree(string path)
    {
        try
        {
            if (Directory.Exists(path) &&
                !Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any())
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // Best-effort: the debris only degrades a later retry's error message.
        }
    }

    /// <summary>
    /// Best-effort removal of a whole file or directory. Only safe when the caller knows the
    /// source is still fully intact (i.e. a copy phase failed before any source deletion).
    /// </summary>
    public static void TryDeleteCopyDebris(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            else if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // Best-effort.
        }
    }
}
