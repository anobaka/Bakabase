using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Moves a frozen current payload selection without replacing existing files.
/// Metadata stays where its original operation wrote it. The caller persists intent before
/// this helper runs and fences task execution and artifact maintenance throughout.</summary>
internal static class AcquisitionStageDirectoryFileMove
{
    internal static (List<AcquisitionStageDirectoryMoveFile> Files, IReadOnlyList<string> EmptyDirectories) Inspect(
        string root, IReadOnlySet<string> excluded, CancellationToken ct)
    {
        var files = new List<AcquisitionStageDirectoryMoveFile>(); var empty = new List<string>();
        var pending = new Stack<(string Path, int Depth)>(); pending.Push((root, 0)); var count = 0;
        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested(); ArtifactManifestFiles.RejectLinks(directory.Path);
            if (++count > 100000 || directory.Depth > 128) throw new InvalidOperationException("STAGE_DIRECTORY_TOO_LARGE");
            var entries = Directory.GetFileSystemEntries(directory.Path);
            if (entries.Length == 0 && directory.Path != root) empty.Add(Path.GetRelativePath(root, directory.Path));
            foreach (var path in entries)
            {
                ct.ThrowIfCancellationRequested(); ArtifactManifestFiles.RejectLinks(path);
                if (excluded.Contains(path)) continue;
                if (Directory.Exists(path)) pending.Push((path, directory.Depth + 1));
                else
                {
                    if (++count > 100000) throw new InvalidOperationException("STAGE_DIRECTORY_TOO_LARGE");
                    var file = new FileInfo(path);
                    files.Add(new(Path.GetRelativePath(root, path), file.Length, file.LastWriteTimeUtc.Ticks));
                }
            }
        }
        return (files.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToList(), empty);
    }

    internal static void CheckTargets(AcquisitionStageDirectoryMove move, IReadOnlySet<string>? replaceableDirectoryBlockers = null)
    {
        foreach (var file in move.Files)
        {
            var target = Resolve(move.Directory, file.RelativePath);
            ArtifactManifestFiles.RejectLinks(target);
            if (File.Exists(target) || Directory.Exists(target)) throw new IOException("STAGE_DIRECTORY_TARGET_CONFLICT");
            for (var parent = Path.GetDirectoryName(target); parent != null && !Same(parent, move.Directory); parent = Path.GetDirectoryName(parent))
                if (File.Exists(parent) && replaceableDirectoryBlockers?.Contains(parent) != true) throw new IOException("STAGE_DIRECTORY_TARGET_CONFLICT");
        }
        foreach (var empty in move.EmptyDirectories)
            if (File.Exists(Resolve(move.Directory, empty)) && replaceableDirectoryBlockers?.Contains(Resolve(move.Directory, empty)) != true)
                throw new IOException("STAGE_DIRECTORY_TARGET_CONFLICT");
    }

    internal static async Task ExecuteAsync(AcquisitionStageDirectoryMove move, string journalId,
        IFileArtifactMaterializer materializer, Func<Task> persist, Func<Task> guard, CancellationToken ct)
    {
        if (!move.MoveFiles || move.SourceDirectory == null) return;
        ArtifactManifestFiles.RejectLinks(move.SourceDirectory);
        if (!Directory.Exists(move.SourceDirectory)) throw new IOException("STAGE_DIRECTORY_SOURCE_UNAVAILABLE");
        ArtifactManifestFiles.RejectLinks(move.Directory); Directory.CreateDirectory(move.Directory);
        for (var index = 0; index < move.Files.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var item = move.Files[index];
            if (item.Completed) continue;
            var source = Resolve(move.SourceDirectory, item.RelativePath); var target = Resolve(move.Directory, item.RelativePath);
            ArtifactManifestFiles.RejectLinks(source); ArtifactManifestFiles.RejectLinks(target);
            // An exclusive native rename may have completed just before process shutdown.
            // Recognizing that outcome never overwrites or deletes a destination.
            if (!File.Exists(source))
            {
                if (!Matches(target, item) || item.CopiedHash != null && await HashAsync(target, ct) != item.CopiedHash)
                    throw new IOException("STAGE_DIRECTORY_MOVE_CHANGED");
                move.Files[index] = item with {Completed = true}; continue;
            }
            if (!Matches(source, item)) throw new IOException("STAGE_DIRECTORY_MOVE_CHANGED");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target))
            {
                if (item.CopiedHash == null || await HashAsync(source, ct) != item.CopiedHash ||
                    await HashAsync(target, ct) != item.CopiedHash) throw new IOException("STAGE_DIRECTORY_TARGET_CONFLICT");
                await guard();
                if (!Matches(source, item) || await HashAsync(source, ct) != item.CopiedHash)
                    throw new IOException("STAGE_DIRECTORY_MOVE_CHANGED");
                File.Delete(source);
            }
            else if (!await materializer.TryMoveAsync(source, target, ct))
            {
                // Cross-volume fallback is copy -> verify -> persist -> exclusive publish -> unlink.
                // Its private temporary file is tied to this durable intent and is never a final output.
                var privateDirectory = Path.Combine(move.Directory, ".bakabase-directory-move-" + journalId + "-" + ArtifactManifestFiles.Hash(item.RelativePath)[..16]);
                var temporary = Path.Combine(privateDirectory, "payload");
                var ownerFile = Path.Combine(privateDirectory, "owner.json");
                var owner = JsonSerializer.Serialize(new {journalId, move.SourceDirectory, move.Directory,
                    item.RelativePath, item.Length, item.LastWriteUtcTicks});
                ArtifactManifestFiles.RejectLinks(privateDirectory);
                if (!Directory.Exists(privateDirectory))
                {
                    Directory.CreateDirectory(privateDirectory);
                    await using var owned = new FileStream(ownerFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    await JsonSerializer.SerializeAsync(owned, owner, cancellationToken: ct); await owned.FlushAsync(ct);
                }
                ArtifactManifestFiles.RejectLinks(ownerFile); ArtifactManifestFiles.RejectLinks(temporary);
                if (!await MatchesOwnerAsync(ownerFile, owner, ct) ||
                    Directory.EnumerateFileSystemEntries(privateDirectory).Any(p => p != ownerFile && p != temporary))
                    throw new IOException("STAGE_DIRECTORY_MOVE_INTERRUPTED");
                await persist(); // Private owner proof is durable before the first copied byte.
                if (File.Exists(temporary))
                {
                    if (item.CopiedHash == null)
                    {
                        if (!Matches(source, item)) throw new IOException("STAGE_DIRECTORY_MOVE_CHANGED");
                        File.Delete(temporary); // Only this receipt-owned unfinished copy.
                    }
                    else if (await HashAsync(temporary, ct) != item.CopiedHash) throw new IOException("STAGE_DIRECTORY_MOVE_CHANGED");
                }
                if (!File.Exists(temporary))
                {
                    await materializer.MaterializeAsync(source, temporary, "auto", ct);
                    var hash = await HashAsync(source, ct);
                    if (!Matches(source, item) || await HashAsync(temporary, ct) != hash)
                        throw new IOException("STAGE_DIRECTORY_MOVE_CHANGED");
                    File.SetLastWriteTimeUtc(temporary, new DateTime(item.LastWriteUtcTicks, DateTimeKind.Utc));
                    item = item with {CopiedHash = hash}; move.Files[index] = item; await persist();
                }
                if (await HashAsync(source, ct) != item.CopiedHash) throw new IOException("STAGE_DIRECTORY_MOVE_CHANGED");
                await guard();
                if (!await materializer.TryMoveAsync(temporary, target, ct))
                    throw new IOException("STAGE_DIRECTORY_MOVE_INTERRUPTED");
                if (!Matches(source, item) || await HashAsync(source, ct) != item.CopiedHash)
                    throw new IOException("STAGE_DIRECTORY_MOVE_CHANGED");
                File.Delete(source);
                File.Delete(ownerFile); Directory.Delete(privateDirectory);
            }
            move.Files[index] = item with {Completed = true};
            // Atomic moves are cheap. Batch metadata writes while keeping recovery possible
            // from the frozen identity of every pending file.
            if ((index + 1) % 64 == 0) await persist();
        }
        await persist();
        foreach (var item in move.Files.Where(f => f.Completed))
        {
            var directory = Path.Combine(move.Directory, ".bakabase-directory-move-" + journalId + "-" + ArtifactManifestFiles.Hash(item.RelativePath)[..16]);
            if (!Directory.Exists(directory)) continue;
            var marker = Path.Combine(directory, "owner.json"); ArtifactManifestFiles.RejectLinks(directory); ArtifactManifestFiles.RejectLinks(marker);
            var expected = JsonSerializer.Serialize(new {journalId, move.SourceDirectory, move.Directory,
                item.RelativePath, item.Length, item.LastWriteUtcTicks});
            if (!await MatchesOwnerAsync(marker, expected, ct) ||
                Directory.EnumerateFileSystemEntries(directory).Any(p => p != marker))
                throw new IOException("STAGE_DIRECTORY_MOVE_INTERRUPTED");
            File.Delete(marker); Directory.Delete(directory);
        }
        foreach (var empty in move.EmptyDirectories) Directory.CreateDirectory(Resolve(move.Directory, empty));
        // Only prune now-empty payload ancestors. Never remove the registered root, metadata,
        // unknown user files or the parent of the source selection.
        var parents = move.Files.Select(f => Path.GetDirectoryName(Resolve(move.SourceDirectory, f.RelativePath)))
            .Concat(move.EmptyDirectories.Select(p => Resolve(move.SourceDirectory, p))).Where(p => p != null)
            .Cast<string>().OrderByDescending(p => p.Length).ToArray();
        foreach (var first in parents)
            for (var path = first; !Same(path, move.SourceDirectory) && ArtifactCleanupPathReferences.StrictlyUnder(path, move.SourceDirectory); path = Path.GetDirectoryName(path)!)
            {
                ArtifactManifestFiles.RejectLinks(path);
                if (!Directory.Exists(path)) continue;
                if (Directory.EnumerateFileSystemEntries(path).Any()) break;
                Directory.Delete(path);
            }
    }

    private static async Task<bool> MatchesOwnerAsync(string marker, string expected, CancellationToken ct)
    {
        if (!File.Exists(marker) || new FileInfo(marker).Length > 16384) return false;
        try {return JsonSerializer.Deserialize<string>(await File.ReadAllTextAsync(marker, ct)) == expected;}
        catch (JsonException) {return false;}
    }

    private static bool Matches(string path, AcquisitionStageDirectoryMoveFile item)
    {
        var file = new FileInfo(path);
        return file.Exists && file.Length == item.Length && file.LastWriteTimeUtc.Ticks == item.LastWriteUtcTicks;
    }
    private static async Task<string> HashAsync(string path, CancellationToken ct)
    { await using var stream = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)); }
    internal static bool Same(string a, string b) => ArtifactManifestFiles.PathComparer.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)));
    internal static string Resolve(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!ArtifactCleanupPathReferences.StrictlyUnder(path, Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))))
            throw new InvalidDataException("STAGE_DIRECTORY_MOVE_CHANGED");
        return path;
    }
}
