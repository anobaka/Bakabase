using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.InsideWorld.Business.Components.Artifacts;

/// <summary>One exact-file manifest implementation shared by downloads and reusable operations.</summary>
public static class ArtifactManifestFiles
{
    public static StringComparer PathComparer => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static Task<ArtifactManifest> CaptureAsync(string rootDirectory, IReadOnlyCollection<string> files,
        CancellationToken ct = default, IReadOnlyCollection<string>? directories = null) =>
        CaptureAsync(rootDirectory, files, path => new FileInfo(path), ct: ct, directories: directories);

    internal static async Task<ArtifactManifest> CaptureAsync(string rootDirectory, IReadOnlyCollection<string> files,
        Func<string, FileInfo> fileInformation,
        Func<Stream, CancellationToken, ValueTask<byte[]>>? hashData = null, CancellationToken ct = default,
        IReadOnlyCollection<string>? directories = null)
    {
        var root = Absolute(rootDirectory);
        if (files.Count == 0) throw new InvalidDataException("An artifact must contain at least one file.");
        var paths = files.Select(Absolute).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (paths.Distinct(PathComparer).Count() != paths.Length)
            throw new InvalidDataException("An artifact manifest must not contain duplicate paths.");
        var entries = new List<ArtifactManifestEntry>();
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            var relative = RelativeFile(root, path);
            RejectLinks(path);
            var information = fileInformation(path);
            // Exists suppresses access and storage errors. Open the file so a disconnected
            // share or denied read cannot masquerade as a confirmed missing download.
            // ArtifactContentHash supplies its own bounded, full-size sequential buffer.
            // Avoid runtime HashDataAsync's small reads on network-mounted files.
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                1, FileOptions.Asynchronous | FileOptions.SequentialScan);
            // SMB can expose a cached directory timestamp until this read handle opens,
            // even after the writer has flushed and closed. Take the baseline from the
            // opened file, then require both that handle and its path to remain stable.
            var size = RandomAccess.GetLength(stream.SafeFileHandle);
            var written = File.GetLastWriteTimeUtc(stream.SafeFileHandle);
            var hash = Convert.ToHexString(await (hashData?.Invoke(stream, ct) ?? ArtifactContentHash.ComputeAsync(stream, ct)))
                .ToLowerInvariant();
            information.Refresh();
            RejectLinks(path);
            if (!information.Exists || size != information.Length || written != information.LastWriteTimeUtc ||
                size != RandomAccess.GetLength(stream.SafeFileHandle) ||
                written != File.GetLastWriteTimeUtc(stream.SafeFileHandle))
                throw new IOException("An artifact file changed while its manifest was being recorded.");
            entries.Add(new(relative, size, hash));
        }
        var relativeDirectories = directories?.Select(path => RelativeFile(root, Absolute(path)))
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var manifest = new ArtifactManifest(entries, Fingerprint(entries, relativeDirectories), relativeDirectories);
        VerifyDirectories(root, manifest);
        return manifest;
    }

    /// <summary>Preserves the historical DownloadResultService relative-path/SHA256 fingerprint format.</summary>
    public static string Fingerprint(IEnumerable<ArtifactManifestEntry> entries) =>
        Hash(JsonSerializer.Serialize(entries.Select(e => new[] {e.RelativePath, e.Sha256}).ToArray()));

    public static string Fingerprint(IEnumerable<ArtifactManifestEntry> entries, IReadOnlyList<string>? directories) =>
        directories is not {Count: > 0} ? Fingerprint(entries) : Hash("artifact-directories-v1\n" +
            JsonSerializer.Serialize(entries.Select(e => new[] {e.RelativePath, e.Sha256}).ToArray()) + "\n" +
            JsonSerializer.Serialize(directories.OrderBy(path => path, StringComparer.Ordinal).ToArray()));

    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static IReadOnlyList<string> ResolveFiles(string rootDirectory, ArtifactManifest manifest)
    {
        var root = Absolute(rootDirectory);
        if (manifest.Entries == null || manifest.Entries.Count == 0 ||
            manifest.Entries.Any(e => e == null || e.Length < 0 || e.Sha256?.Length != 64) ||
            !string.Equals(Fingerprint(manifest.Entries, manifest.Directories), manifest.Fingerprint, StringComparison.Ordinal))
            throw new InvalidDataException("The artifact manifest is invalid.");
        var files = manifest.Entries.Select(entry =>
        {
            var relative = entry.RelativePath;
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains('\\') ||
                relative.Contains(':') || relative.Split('/').Any(p => p is "" or "." or ".."))
                throw new InvalidDataException("The artifact manifest contains an unsafe relative path.");
            var path = Path.GetFullPath(Path.Combine(root, relative));
            RelativeFile(root, path);
            return path;
        }).ToArray();
        if (files.Distinct(PathComparer).Count() != files.Length)
            throw new InvalidDataException("The artifact manifest contains duplicate paths.");
        _ = DirectoryPaths(root, manifest);
        return files;
    }

    public static IReadOnlyList<string> ResolveDirectories(string rootDirectory, ArtifactManifest manifest)
    {
        _ = ResolveFiles(rootDirectory, manifest);
        return DirectoryPaths(Absolute(rootDirectory), manifest);
    }

    private static IReadOnlyList<string> DirectoryPaths(string root, ArtifactManifest manifest)
    {
        var directories = manifest.Directories ?? [];
        if (directories.Count > 10000) throw new InvalidDataException("The directory manifest exceeds its limit.");
        var fileNames = manifest.Entries.Select(file => file.RelativePath).ToHashSet(PathComparer);
        var paths = directories.Select(relative =>
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains('\\') ||
                relative.Contains(':') || relative.Any(char.IsControl) || relative.Split('/').Any(p => p is "" or "." or ".."))
                throw new InvalidDataException("The directory manifest contains an unsafe relative path.");
            for (var ancestor = relative; ancestor.Length > 0;)
            {
                if (fileNames.Contains(ancestor)) throw new InvalidDataException("A manifest directory conflicts with a file.");
                var separator = ancestor.LastIndexOf('/');
                if (separator < 0) break;
                ancestor = ancestor[..separator];
            }
            var path = Path.GetFullPath(Path.Combine(root, relative));
            RelativeFile(root, path);
            return path;
        }).ToArray();
        if (paths.Distinct(PathComparer).Count() != paths.Length)
            throw new InvalidDataException("The directory manifest contains duplicate paths.");
        return paths;
    }

    public static void VerifyDirectories(string rootDirectory, ArtifactManifest manifest)
    {
        foreach (var path in DirectoryPaths(Absolute(rootDirectory), manifest))
            VerifyDirectory(path);
    }

    private static void VerifyDirectory(string path)
    {
        RejectLinks(path);
        if (!File.GetAttributes(path).HasFlag(FileAttributes.Directory))
            throw new InvalidDataException("A recorded artifact directory was replaced with a file.");
    }

    public static void CreateDirectories(string rootDirectory, ArtifactManifest manifest)
    {
        // Validate the complete projection before creating the first directory.
        var paths = ResolveDirectories(rootDirectory, manifest);
        RejectLinks(rootDirectory);
        foreach (var path in paths)
        {
            RejectLinks(path);
            if (File.Exists(path)) throw new InvalidDataException("An artifact directory conflicts with an existing file.");
        }
        foreach (var path in paths)
        {
            RejectLinks(path);
            Directory.CreateDirectory(path);
            RejectLinks(path);
        }
    }

    /// <summary>Only for a newly created, exclusively owned extraction output, never a legacy/shared root.</summary>
    public static IReadOnlyList<string> CaptureEmptyDirectories(string rootDirectory, CancellationToken ct = default)
    {
        var root = Absolute(rootDirectory);
        var found = new List<string>();
        var visited = 0;
        void Visit(string directory)
        {
            ct.ThrowIfCancellationRequested();
            RejectLinks(directory);
            if (++visited > 10000) throw new InvalidDataException("The extraction directory count exceeds its limit.");
            var entries = Directory.EnumerateFileSystemEntries(directory).ToArray();
            if (entries.Length == 0 && !PathComparer.Equals(root, directory)) found.Add(directory);
            foreach (var path in entries)
            {
                RejectLinks(path);
                if (File.GetAttributes(path).HasFlag(FileAttributes.Directory)) Visit(path);
            }
        }
        Visit(root);
        return found.OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }

    public static Task<ArtifactValidation> ValidateAsync(string rootDirectory, ArtifactManifest manifest,
        CancellationToken ct = default) => ValidateAsync(rootDirectory, manifest,
        (root, files, token) => CaptureAsync(root, files, token), ct);

    internal static async Task<ArtifactValidation> ValidateAsync(string rootDirectory, ArtifactManifest manifest,
        Func<string, IReadOnlyCollection<string>, CancellationToken, Task<ArtifactManifest>> capture,
        CancellationToken ct = default)
    {
        string? currentPath = null;
        try
        {
            var files = ResolveFiles(rootDirectory, manifest);
            foreach (var path in ResolveDirectories(rootDirectory, manifest))
            {
                currentPath = path;
                ct.ThrowIfCancellationRequested();
                VerifyDirectory(path);
            }
            for (var index = 0; index < files.Count; index++)
            {
                var path = currentPath = files[index];
                ct.ThrowIfCancellationRequested();
                RejectLinks(path);
                var file = new FileInfo(path);
                if (file.Length != manifest.Entries[index].Length)
                    return new(ArtifactLocationState.Modified, "An artifact file has changed: " + path);
                var captured = await capture(rootDirectory, new[] {path}, ct);
                if (captured.Entries[0].Sha256 != manifest.Entries[index].Sha256)
                    return new(ArtifactLocationState.Modified, "An artifact file has changed: " + path);
            }
            return new(ArtifactLocationState.Available);
        }
        catch (FileNotFoundException error)
        {
            return new(IsMissingFileConfirmed(rootDirectory, error.FileName ?? currentPath, ct)
                ? ArtifactLocationState.Missing : ArtifactLocationState.StorageUnavailable, error.Message);
        }
        catch (DirectoryNotFoundException error)
        {
            return new(IsMissingFileConfirmed(rootDirectory, currentPath, ct)
                ? ArtifactLocationState.Missing : ArtifactLocationState.StorageUnavailable, error.Message);
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException)
        { return new(ArtifactLocationState.Modified, error.Message); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return new(ArtifactLocationState.StorageUnavailable, error.Message); }
    }

    /// <summary>
    /// Confirm absence only inside an accessible root and immediate parent. A missing
    /// mount/directory or unreadable listing is uncertainty, not permission to reacquire.
    /// </summary>
    public static bool IsMissingFileConfirmed(string rootDirectory, string? path, CancellationToken ct = default)
    {
        if (path == null) return false;
        try
        {
            ct.ThrowIfCancellationRequested();
            var root = Absolute(rootDirectory);
            path = Absolute(path);
            RelativeFile(root, path);
            var parent = Path.GetDirectoryName(path)!;
            RejectLinks(root);
            RejectLinks(parent);
            if (!File.GetAttributes(root).HasFlag(FileAttributes.Directory) ||
                !File.GetAttributes(parent).HasFlag(FileAttributes.Directory)) return false;
            // Enumerate rather than Exists, which turns I/O/access failures into false.
            foreach (var entry in Directory.EnumerateFileSystemEntries(parent))
            {
                ct.ThrowIfCancellationRequested();
                if (PathComparer.Equals(Absolute(entry), path)) return false;
            }
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { return false; }
    }

    public static string Absolute(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new InvalidDataException("An artifact path must be absolute.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    public static string RelativeFile(string rootDirectory, string path)
    {
        var relative = Path.GetRelativePath(rootDirectory, path);
        if (Path.IsPathRooted(relative) || relative == "." || relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("Every artifact file must be inside its root directory.");
        return relative.Replace('\\', '/');
    }

    public static void RejectLinks(string path)
    {
        FileSystemInfo? current = File.Exists(path) ? new FileInfo(path) : new DirectoryInfo(path);
        while (current != null)
        {
            if (current.LinkTarget != null || (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint)))
                throw new InvalidDataException("Artifact paths must not pass through symbolic links.");
            current = current is DirectoryInfo directory ? directory.Parent : ((FileInfo) current).Directory;
        }
    }
}
