using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bakabase.Abstractions.Components.FileSystem;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components;

/// <summary>Stops automatic admission to a full destination volume. A failure does not disable
/// queued tasks or affect other volumes. Explicit continuation rechecks that destination first.</summary>
public sealed class DownloadStorageAdmission
{
    public const long MinimumFreeBytes = 64L * 1024 * 1024;
    private sealed record Hold(Guid Id);
    private readonly ConcurrentDictionary<string, Hold> _holds = new(PathComparer);
    private readonly ConcurrentDictionary<string, string> _admittedVolumes = new(PathComparer);
    private readonly Func<string, string> _volume;
    private readonly Func<string, long> _available;
    private readonly Action<string> _probe;
    private readonly object _mountGate = new();
    private string[] _mounts = [];
    private DateTime _mountsAt;
    private readonly bool _native;

    private static StringComparer PathComparer => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static StringComparison PathComparison => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public DownloadStorageAdmission()
    {
        _native = true;
        _volume = NativeVolume;
        _available = root => new DriveInfo(root).AvailableFreeSpace;
        _probe = ProbeWrite;
    }

    internal DownloadStorageAdmission(Func<string, string> volume, Func<string, long> available,
        Action<string> probe)
    {
        _volume = volume;
        _available = available;
        _probe = probe;
    }

    public string? BlockReason(string path)
    {
        if (_holds.IsEmpty) return null;
        try
        {
            var full = ResolveExistingPath(path);
            var root = _volume(full);
            return _holds.ContainsKey(root) || _holds.Keys.Any(held => held.Length > root.Length && Within(full, held))
                ? "LOCAL_STORAGE_FULL" : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A temporarily missing mount must not turn its held queue back into new writes.
            return _holds.Keys.Any(root => Within(path, root)) ? "LOCAL_STORAGE_FULL" : null;
        }
    }

    public void ObserveFailure(string path, Exception error)
    {
        if (LocalStorageFailures.Code(error) != "LOCAL_STORAGE_FULL") return;
        try
        {
            var requested = Path.GetFullPath(path);
            var root = _admittedVolumes.TryGetValue(requested, out var admitted)
                ? admitted : _volume(ResolveExistingPath(requested));
            _holds[root] = new Hold(Guid.NewGuid());
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Reporting the original failure must still complete even if the mount disappeared.
        }
    }

    public void BeforeStart(string path, bool explicitContinuation, bool verifyWritable = false)
    {
        var requested = Path.GetFullPath(path);
        var full = ResolveExistingPath(requested);
        // Admission and explicit recovery must see current mount topology, not a cached parent.
        if (_native) lock (_mountGate) _mountsAt = DateTime.MinValue;
        var root = _volume(full);
        if ((_admittedVolumes.TryGetValue(requested, out var original) && !PathComparer.Equals(root, original)) ||
            _holds.Keys.Any(held => !PathComparer.Equals(held, root) && Within(full, held)))
            throw new LocalStorageException("LOCAL_STORAGE_UNAVAILABLE");
        _holds.TryGetValue(root, out var observed);
        if (observed != null && !explicitContinuation) throw new LocalStorageException("LOCAL_STORAGE_FULL");
        try
        {
            // This is a small admission reserve, not a promise that the entire transfer fits.
            // A later ENOSPC still terminates that task and holds subsequent tasks on this volume.
            if (_available(root) < MinimumFreeBytes) throw new LocalStorageException("LOCAL_STORAGE_FULL");
            if (explicitContinuation && (observed != null || verifyWritable)) _probe(full);
            if (observed != null)
                ((ICollection<KeyValuePair<string, Hold>>)_holds).Remove(new(root, observed));
            // Do not erase a newer failure reported concurrently with the explicit write probe.
            if (_holds.ContainsKey(root)) throw new LocalStorageException("LOCAL_STORAGE_FULL");
            _admittedVolumes[requested] = root;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            var code = LocalStorageFailures.Code(error) ?? "LOCAL_STORAGE_WRITE_FAILED";
            if (code == "LOCAL_STORAGE_FULL") _holds[root] = new Hold(Guid.NewGuid());
            if (error is LocalStorageException) throw;
            throw new LocalStorageException(code, error);
        }
    }

    private string NativeVolume(string path)
    {
        lock (_mountGate)
        {
            if (_mounts.Length == 0 || DateTime.UtcNow - _mountsAt > TimeSpan.FromSeconds(15))
            {
                _mounts = DriveInfo.GetDrives().Select(d => Path.GetFullPath(d.Name))
                    .OrderByDescending(n => n.Length).ToArray();
                _mountsAt = DateTime.UtcNow;
            }
            return _mounts.FirstOrDefault(root => Within(path, root)) ??
                   throw new LocalStorageException("LOCAL_STORAGE_UNAVAILABLE");
        }
    }

    internal static string ResolveExistingPath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var segment in full[root.Length..].Split(Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            var directory = new DirectoryInfo(current);
            if (directory.LinkTarget != null)
            {
                var target = directory.ResolveLinkTarget(returnFinalTarget: true);
                if (target == null || !target.Exists)
                    throw new LocalStorageException("LOCAL_STORAGE_UNAVAILABLE");
                current = target.FullName;
            }
        }
        return current;
    }

    private static bool Within(string path, string root) =>
        string.Equals(path.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), PathComparison) ||
        path.StartsWith(root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar, PathComparison);

    private static void ProbeWrite(string path)
    {
        var directory = path;
        while (!Directory.Exists(directory))
            directory = Path.GetDirectoryName(directory) ?? throw new LocalStorageException("LOCAL_STORAGE_UNAVAILABLE");
        var probe = Path.Combine(directory, ".bakabase-storage-probe-" + Guid.NewGuid().ToString("N"));
        using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            4096, FileOptions.DeleteOnClose | FileOptions.WriteThrough);
        stream.Write(new byte[4096]);
        stream.Flush(flushToDisk: true);
    }
}
