using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.Acquisition.Planning;

internal static class SupersededDeliveryFiles
{
    public static async Task CheckAsync(string root, ArtifactManifest manifest, bool partial, CancellationToken ct)
    {
        var files = Inventory(root, manifest, ct);
        var entries = manifest.Entries.ToDictionary(e => e.RelativePath, ArtifactManifestFiles.PathComparer);
        if (!partial && entries.Keys.Any(key => !files.Contains(key)))
            throw new InvalidDataException("The previous delivery is missing registered files.");
        foreach (var path in files)
            await CheckFile(Path.Combine(root, path), entries.GetValueOrDefault(path), ct);
    }

    public static async Task PurgeAsync(string root, ArtifactManifest manifest, CancellationToken ct)
    {
        // Preflight every remaining entry before the first unlink, then recheck each individual file.
        // A retry after interruption can account for already removed files from the same receipt.
        await CheckAsync(root, manifest, partial: true, ct);
        var entries = manifest.Entries.ToDictionary(e => e.RelativePath, ArtifactManifestFiles.PathComparer);
        var files = Inventory(root, manifest, ct);
        foreach (var relative in files)
        {
            var path = Path.Combine(root, relative);
            await CheckFile(path, entries.GetValueOrDefault(relative), ct);
            ArtifactManifestFiles.RejectLinks(path);
            File.Delete(path);
        }
        // Never recurse through new entries that appeared after preflight.
        foreach (var dir in Directories(manifest).OrderByDescending(s => s.Length))
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.Combine(root, dir);
            ArtifactManifestFiles.RejectLinks(path);
            if (Directory.Exists(path)) Directory.Delete(path, false);
        }
        ArtifactManifestFiles.RejectLinks(root);
        Directory.Delete(root, false);
    }

    private static HashSet<string> Directories(ArtifactManifest manifest)
    {
        var directories = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        foreach (var file in manifest.Entries)
            for (var dir = Path.GetDirectoryName(file.RelativePath); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
                directories.Add(dir.Replace('\\', '/'));
        return directories;
    }

    private static HashSet<string> Inventory(string root, ArtifactManifest manifest, CancellationToken ct)
    {
        ArtifactManifestFiles.RejectLinks(root);
        _ = ArtifactManifestFiles.ResolveFiles(root, manifest);
        var files = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        var directories = Directories(manifest);
        var allowed = manifest.Entries.Select(e => e.RelativePath).ToHashSet(ArtifactManifestFiles.PathComparer);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var folder))
        {
            ArtifactManifestFiles.RejectLinks(folder);
            foreach (var path in Directory.EnumerateFileSystemEntries(folder))
            {
                ct.ThrowIfCancellationRequested();
                ArtifactManifestFiles.RejectLinks(path);
                var key = ArtifactManifestFiles.RelativeFile(root, path);
                var attributes = File.GetAttributes(path);
                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    if (!directories.Contains(key)) throw new InvalidDataException("Unregistered directory retained: " + key);
                    pending.Push(path);
                }
                else
                {
                    if (!allowed.Contains(key) && !string.Equals(Path.GetFileName(path), ".DS_Store", StringComparison.Ordinal))
                        throw new InvalidDataException("Unregistered file retained: " + key);
                    files.Add(key);
                }
            }
        }
        return files;
    }

    private static async Task CheckFile(string path, ArtifactManifestEntry? entry, CancellationToken ct)
    {
        ArtifactManifestFiles.RejectLinks(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true);
        if (entry == null)
        {
            // Finder's desktop database is the only unregistered file accepted. Both its basename
            // and binary header must match; a user file carrying this name is otherwise preserved.
            var header = new byte[8];
            if (Path.GetFileName(path) != ".DS_Store" || stream.Length is < 8 or > 16 * 1024 * 1024 ||
                await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct) != header.Length ||
                !header.AsSpan().SequenceEqual("\0\0\0\u0001Bud1"u8))
                throw new InvalidDataException("Unregistered file retained: " + path);
            return;
        }
        if (stream.Length != entry.Length || !Convert.ToHexString(await SHA256.HashDataAsync(stream, ct))
                .Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase) || stream.Length != entry.Length)
            throw new InvalidDataException("The contents changed; this file was retained: " + path);
    }
}
