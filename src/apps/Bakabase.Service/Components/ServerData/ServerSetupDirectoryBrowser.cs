using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;

namespace Bakabase.Service.Components.ServerData;

/// <summary>A bounded, read-only directory listing that requires the setup session's capability.</summary>
public static class ServerSetupDirectoryBrowser
{
    public const int MaxDirectories = 200;
    public const int MaxScannedEntries = 4000;
    private static readonly TimeSpan ScanBudget = TimeSpan.FromMilliseconds(250);

    public sealed record Folder(string Name, string Path);
    public sealed record Result(string CurrentPath, string? ParentPath, Folder[] Roots, Folder[] Directories,
        bool Truncated, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CandidatePath = null);
    public sealed class AccessDeniedException(string message, Exception inner) : IOException(message, inner);

    internal static Result Read(string currentPath, string? requestedPath, string? newFolderName,
        CancellationToken cancellationToken)
    {
        if (requestedPath?.Length > 4096) throw new ArgumentException("The directory path is too long (maximum 4096 characters).");
        ValidateFolderName(newFolderName);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = ServerSetupSession.Canonical(string.IsNullOrWhiteSpace(requestedPath) ? currentPath : requestedPath);
            var folders = new List<Folder>();
            var scanned = 0;
            var truncated = false;
            var clock = Stopwatch.StartNew();
            // Enumerate entries, not just directories, so millions of files cannot cause
            // an unbounded scan before the first directory is yielded. Network filesystem
            // calls themselves remain subject to the operating system's I/O timeout.
            foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", new EnumerationOptions
                     { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0 }))
            {
                cancellationToken.ThrowIfCancellationRequested();
                scanned++;
                try
                {
                    if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0)
                        folders.Add(new Folder(Path.GetFileName(entry), ServerSetupSession.Canonical(entry)));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { /* A child can disappear or become inaccessible during enumeration. */ }
                if (folders.Count >= MaxDirectories || scanned >= MaxScannedEntries || clock.Elapsed >= ScanBudget)
                {
                    truncated = true;
                    break;
                }
            }
            var roots = OperatingSystem.IsWindows() ? Directory.GetLogicalDrives() : ["/"];
            return new Result(path, Directory.GetParent(path)?.FullName,
                roots.Select(root => new Folder(root, root)).ToArray(),
                folders.OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase).ToArray(), truncated,
                newFolderName == null ? null : ServerSetupSession.Canonical(Path.Combine(path, newFolderName)));
        }
        catch (UnauthorizedAccessException error)
        {
            throw new AccessDeniedException("The server does not have permission to read this folder. Choose another folder or adjust its access permissions.", error);
        }
    }

    private static void ValidateFolderName(string? name)
    {
        if (name == null) return;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name is "." or ".." ||
            name != name.Trim() || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Contains('/') || name.Contains('\\') || name.Any(char.IsControl))
            throw new ArgumentException("Use one folder name, up to 255 characters, without slashes, leading or trailing spaces, or parent-directory notation.");
        if (OperatingSystem.IsWindows())
        {
            var basename = name.Split('.')[0].ToUpperInvariant();
            if (name.EndsWith('.') || basename is "CON" or "PRN" or "AUX" or "NUL" ||
                basename.Length == 4 && (basename.StartsWith("COM", StringComparison.Ordinal) || basename.StartsWith("LPT", StringComparison.Ordinal)) &&
                basename[3] is >= '1' and <= '9')
                throw new ArgumentException("This folder name is reserved by Windows. Choose a different name.");
        }
    }
}
