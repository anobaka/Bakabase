using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.ResourceMove;
using Bootstrap.Components.Tasks;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.ResourceMove;

/// <summary>A conflict is data, not a failed copy. No source content has been removed when
/// discovered during preflight. A conflict after physical preparation retains its journal.</summary>
public sealed class ResourceMoveConflictException(string kind, string path, bool canOverwrite, string fingerprint)
    : IOException($"Move conflict ({kind}): {path}")
{
    public string Kind { get; } = kind;
    public string Path { get; } = path;
    public bool CanOverwrite { get; } = canOverwrite;
    public string Fingerprint { get; } = fingerprint;
}

/// <summary>
/// Copies into a unique sibling staging directory before publishing any content. Each target
/// replacement is atomic and has a journaled backup. Source removal happens only after every
/// target was verified. Recovery never treats arbitrary existing destination data as ours.
/// </summary>
public static class ResourceMoveSafeFileSystem
{
    public sealed class Journal
    {
        public string StageRoot { get; set; } = null!;
        public string SourcePath { get; set; } = null!;
        public string DestPath { get; set; } = null!;
        public bool IsDirectory { get; set; }
        public bool SourceDeleted { get; set; }
        public bool Staged { get; set; }
        public bool Published { get; set; }
        public bool NativeRename { get; set; }
        public bool NativePublishing { get; set; }
        public string? NativeMarker { get; set; }
        public string? NativeFileStamp { get; set; }
        public DateTime OriginalLastWriteTimeUtc { get; set; }
        public List<string> Directories { get; set; } = [];
        public List<Entry> Files { get; set; } = [];
    }

    public sealed class Entry
    {
        public string RelativePath { get; set; } = null!;
        public long Length { get; set; }
        public string Hash { get; set; } = null!;
        public string DestinationBefore { get; set; } = "missing";
        public bool Committing { get; set; }
        public bool Committed { get; set; }
    }

    public static string Fingerprint(string path)
    {
        if (File.Exists(path))
        {
            var f = new FileInfo(path);
            return $"file:{f.Length}:{f.LastWriteTimeUtc.Ticks}:{HashFile(path)}";
        }
        if (Directory.Exists(path))
        {
            // Includes children so an individual merge authorization cannot silently cover
            // subsequently added content. File replacements have their own stronger stamps.
            var entries = Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories)
                .OrderBy(x => x, StringComparer.Ordinal)
                .Select(x => $"{Path.GetRelativePath(path, x)}:{(File.Exists(x) ? new FileInfo(x).Length : -1)}:{File.GetLastWriteTimeUtc(x).Ticks}");
            return "directory:" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", entries))));
        }
        return "missing";
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool IsOurFile(string path, Entry entry) => File.Exists(path) &&
        new FileInfo(path).Length == entry.Length && HashFile(path) == entry.Hash;

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Symbolic links require manual handling before moving: {path}");
    }

    public static ResourceMoveConflictException? FindConflict(string source, string destination,
        Func<string, string, bool> authorized)
    {
        var directory = Directory.Exists(source);
        if (!directory && !File.Exists(source)) throw new FileNotFoundException("Move source no longer exists", source);
        RejectLink(source);
        if (directory && File.Exists(destination) || !directory && Directory.Exists(destination))
            return new("typeMismatch", destination, false, Fingerprint(destination));
        if (Directory.Exists(destination))
        {
            RejectLink(destination);
            var stamp = Fingerprint(destination);
            if (!authorized(destination, stamp)) return new("destinationExists", destination, true, stamp);
        }
        var entries = directory ? Directory.EnumerateFileSystemEntries(source, "*", SearchOption.AllDirectories) : [source];
        foreach (var path in entries)
        {
            RejectLink(path);
            var target = directory ? Path.Combine(destination, Path.GetRelativePath(source, path)) : destination;
            if (Directory.Exists(path))
            {
                if (File.Exists(target)) return new("typeMismatch", target, false, Fingerprint(target));
            }
            else if (Directory.Exists(target)) return new("typeMismatch", target, false, Fingerprint(target));
            else if (File.Exists(target))
            {
                RejectLink(target);
                var stamp = Fingerprint(target);
                if (!authorized(target, stamp)) return new("fileExists", target, true, stamp);
            }
        }
        return null;
    }

    public static async Task Move(ResourceMoveExecutionState record, Func<Task> save,
        Func<int, Task> progress, PauseToken pause, CancellationToken cancellation,
        Func<string, string, bool> authorized)
    {
        var destinationDirectory = Path.GetDirectoryName(record.DestPath);
        if (destinationDirectory == null || !Directory.Exists(destinationDirectory))
            throw new DirectoryNotFoundException($"Move destination is unavailable: {destinationDirectory}");
        var journal = record.MoveJournalJson == null ? null : JsonConvert.DeserializeObject<Journal>(record.MoveJournalJson);
        async Task Save()
        {
            record.MoveJournalJson = JsonConvert.SerializeObject(journal);
            await save();
        }
        if (journal == null)
        {
            var isDirectory = Directory.Exists(record.SourcePath);
            if (!isDirectory && !File.Exists(record.SourcePath)) throw new FileNotFoundException("Move source no longer exists", record.SourcePath);
            RejectLink(record.SourcePath);
            var native = !Directory.Exists(record.DestPath) && !File.Exists(record.DestPath) &&
                         ResourceMoveFileSystem.AreOnSameFileSystem(record.SourcePath, record.DestPath) == true;
            if (!native)
            {
                var conflict = FindConflict(record.SourcePath, record.DestPath, authorized);
                if (conflict != null) throw conflict;
            }
            journal = new Journal
            {
                SourcePath = record.SourcePath,
                DestPath = record.DestPath,
                IsDirectory = isDirectory,
                StageRoot = Path.Combine(Path.GetDirectoryName(record.DestPath)!, $".bakabase-move-{record.Id}-{Guid.NewGuid():N}"),
                NativeRename = native,
                NativeMarker = native && isDirectory ? $".bakabase-move-owner-{Guid.NewGuid():N}" : null,
                NativeFileStamp = native && !isDirectory ? NativeFileStamp(record.SourcePath) : null,
                OriginalLastWriteTimeUtc = File.GetLastWriteTimeUtc(record.SourcePath)
            };
            if (isDirectory && !native)
                journal.Directories = Directory.EnumerateDirectories(record.SourcePath, "*", SearchOption.AllDirectories)
                    .Select(p => Path.GetRelativePath(record.SourcePath, p)).ToList();
            foreach (var src in native ? [] : isDirectory ? Directory.EnumerateFiles(record.SourcePath, "*", SearchOption.AllDirectories) : [record.SourcePath])
            {
                var relative = isDirectory ? Path.GetRelativePath(record.SourcePath, src) : "content";
                var target = isDirectory ? Path.Combine(record.DestPath, relative) : record.DestPath;
                journal.Files.Add(new Entry { RelativePath = relative, Length = new FileInfo(src).Length,
                    Hash = HashFile(src), DestinationBefore = Fingerprint(target) });
            }
            // Persist ownership before creating any staging data. A crash may leave this empty
            // directory behind, but recovery can identify it without touching foreign targets.
            record.PhysicalMoveStarted = true;
            await Save();
        }
        if (journal.SourcePath != record.SourcePath || journal.DestPath != record.DestPath)
            throw new IOException("Move journal no longer matches resource paths");
        if (journal.NativeRename)
        {
            await MoveNative(journal, Save, pause, cancellation);
            await progress(100);
            return;
        }
        string Source(Entry e) => journal.IsDirectory ? Path.Combine(journal.SourcePath, e.RelativePath) : journal.SourcePath;
        string Target(Entry e) => journal.IsDirectory ? Path.Combine(journal.DestPath, e.RelativePath) : journal.DestPath;
        string Staging(Entry e) => Path.Combine(journal.StageRoot, "content", e.RelativePath);
        string Backup(Entry e) => Path.Combine(journal.StageRoot, "backup", e.RelativePath);
        Directory.CreateDirectory(journal.StageRoot);
        if (!journal.Staged)
        {
            var total = Math.Max(1L, journal.Files.Sum(e => e.Length));
            long done = 0;
            foreach (var entry in journal.Files)
            {
                await pause.WaitWhilePausedAsync(cancellation);
                if (!IsOurFile(Source(entry), entry)) throw new IOException($"Source changed during move: {Source(entry)}");
                var stage = Staging(entry);
                if (!IsOurFile(stage, entry))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(stage)!);
                    await using (var input = new FileStream(Source(entry), FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true))
                    await using (var output = new FileStream(stage, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true))
                    {
                        var buffer = new byte[1024 * 1024];
                        int read;
                        while ((read = await input.ReadAsync(buffer, cancellation)) > 0)
                        {
                            await pause.WaitWhilePausedAsync(cancellation);
                            await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
                            done += read;
                            await progress((int)(done * 80 / total));
                        }
                        await output.FlushAsync(cancellation);
                    }
                    if (!IsOurFile(stage, entry)) throw new IOException($"Staged file verification failed: {Source(entry)}");
                    File.SetLastWriteTimeUtc(stage, File.GetLastWriteTimeUtc(Source(entry)));
                }
                else done += entry.Length;
            }
            journal.Staged = true;
            await Save();
        }
        if (!journal.Published)
        {
            if (journal.IsDirectory)
            {
                Directory.CreateDirectory(journal.DestPath);
                foreach (var dir in journal.Directories) Directory.CreateDirectory(Path.Combine(journal.DestPath, dir));
            }
            foreach (var entry in journal.Files)
            {
                await pause.WaitWhilePausedAsync(cancellation);
                var target = Target(entry);
                var stage = Staging(entry);
                if (entry.Committed || entry.Committing && !File.Exists(stage) && IsOurFile(target, entry))
                {
                    if (!IsOurFile(target, entry)) throw new IOException($"Published destination changed: {target}");
                    entry.Committed = true;
                    await Save();
                    continue;
                }
                var actual = Fingerprint(target);
                if (actual != entry.DestinationBefore)
                    throw new ResourceMoveConflictException("destinationChanged", target, false, actual);
                // Re-read policy before each replacement; turning panel auto-overwrite off
                // never retroactively authorizes an operation that has not started.
                if (actual != "missing" && !authorized(target, actual))
                    throw new ResourceMoveConflictException("fileExists", target, true, actual);
                entry.Committing = true;
                await Save();
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target))
                {
                    var backup = Backup(entry);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Replace(stage, target, backup);
                }
                else File.Move(stage, target, false);
                entry.Committed = true;
                await Save();
            }
            journal.Published = true;
            await Save();
        }
        foreach (var entry in journal.Files)
            if (!IsOurFile(Target(entry), entry)) throw new IOException($"Destination verification failed: {Target(entry)}");
        if (!journal.SourceDeleted)
        {
            // Delete only the original manifest, never newly arrived source files. Check all
            // remaining files before deletion; a crash midway is recoverable from target hashes.
            foreach (var entry in journal.Files.Where(e => File.Exists(Source(e))))
                if (!IsOurFile(Source(entry), entry)) throw new IOException($"Source changed before deletion: {Source(entry)}");
            if (journal.IsDirectory && Directory.Exists(journal.SourcePath))
            {
                var expected = journal.Files.Select(Source).ToHashSet(StringComparer.Ordinal);
                if (Directory.EnumerateFiles(journal.SourcePath, "*", SearchOption.AllDirectories).Any(p => !expected.Contains(p)))
                    throw new IOException($"New files appeared in source: {journal.SourcePath}");
            }
            foreach (var entry in journal.Files) File.Delete(Source(entry));
            if (journal.IsDirectory && Directory.Exists(journal.SourcePath))
            {
                foreach (var dir in Directory.EnumerateDirectories(journal.SourcePath, "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length))
                    Directory.Delete(dir, false);
                Directory.Delete(journal.SourcePath, false);
            }
            journal.SourceDeleted = true;
            await Save();
        }
        await progress(100);
    }

    public static void Cleanup(ResourceMoveExecutionState record)
    {
        var journal = record.MoveJournalJson == null ? null : JsonConvert.DeserializeObject<Journal>(record.MoveJournalJson);
        if (journal?.SourceDeleted == true)
        {
            if (journal.NativeMarker != null)
            {
                var marker = Path.Combine(journal.DestPath, journal.NativeMarker);
                if (File.Exists(marker) && File.ReadAllText(marker) == journal.StageRoot)
                {
                    File.Delete(marker);
                    Directory.SetLastWriteTimeUtc(journal.DestPath, journal.OriginalLastWriteTimeUtc);
                }
            }
            if (Directory.Exists(journal.StageRoot)) Directory.Delete(journal.StageRoot, true);
        }
    }

    private static string NativeFileStamp(string path) =>
        $"{new FileInfo(path).Length}:{File.GetLastWriteTimeUtc(path).Ticks}:{File.GetCreationTimeUtc(path).Ticks}";

    private static async Task MoveNative(Journal journal, Func<Task> save, PauseToken pause, CancellationToken cancellation)
    {
        await pause.WaitWhilePausedAsync(cancellation);
        var payload = Path.Combine(journal.StageRoot, "payload");
        bool Exists(string p) => journal.IsDirectory ? Directory.Exists(p) : File.Exists(p);
        if (journal.Published)
        {
            if (!Exists(journal.DestPath)) throw new IOException("Published move destination is missing");
            return;
        }
        Directory.CreateDirectory(journal.StageRoot);
        if (!journal.Staged && Exists(journal.SourcePath))
        {
            if (Exists(payload)) throw new IOException("Both source and move staging exist; manual recovery required");
            // Two same-filesystem renames require no content scan and no second copy. The
            // intermediate path is exclusively ours and was persisted before the first rename.
            if (journal.IsDirectory) Directory.Move(journal.SourcePath, payload);
            else File.Move(journal.SourcePath, payload, false);
        }
        if (Exists(payload))
        {
            if (journal.NativeMarker != null)
                await File.WriteAllTextAsync(Path.Combine(payload, journal.NativeMarker), journal.StageRoot, cancellation);
            journal.Staged = true;
            await save();
            if (Directory.Exists(journal.DestPath) || File.Exists(journal.DestPath))
                throw new ResourceMoveConflictException("destinationChanged", journal.DestPath, false, Fingerprint(journal.DestPath));
            journal.NativePublishing = true;
            await save();
            if (journal.IsDirectory) Directory.Move(payload, journal.DestPath);
            else File.Move(payload, journal.DestPath, false);
        }
        else
        {
            // Recovery after publish but before its database checkpoint. The directory marker
            // proves ownership without hashing gigabytes; files preserve their metadata stamp.
            var owned = journal.IsDirectory && journal.NativeMarker != null
                ? File.Exists(Path.Combine(journal.DestPath, journal.NativeMarker)) &&
                  File.ReadAllText(Path.Combine(journal.DestPath, journal.NativeMarker)) == journal.StageRoot
                : File.Exists(journal.DestPath) && NativeFileStamp(journal.DestPath) == journal.NativeFileStamp;
            if (!journal.NativePublishing || !owned) throw new IOException("Cannot identify native move output; recovery required");
        }
        journal.Published = true;
        journal.SourceDeleted = true;
        await save();
    }
}
