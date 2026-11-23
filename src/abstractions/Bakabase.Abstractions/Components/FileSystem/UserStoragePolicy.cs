using System.Text.Json;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Exceptions;

namespace Bakabase.Abstractions.Components.FileSystem;

/// <summary>
/// Keeps container user files on persistent directory mounts. Native processes retain
/// ordinary filesystem access; this is independent of whether they have a desktop.
/// </summary>
public sealed class UserStoragePolicy : IUserStoragePolicy
{
    private const int MaximumMountInfoLength = 4 * 1024 * 1024;
    private static readonly string[] SystemDirectories =
        ["/proc", "/sys", "/dev", "/etc", "/bin", "/sbin", "/lib", "/lib64", "/usr", "/run"];
    private static readonly HashSet<string> PersistentFileSystems = new(StringComparer.Ordinal)
    {
        "ext2", "ext3", "ext4", "xfs", "btrfs", "zfs", "apfs", "hfs", "hfsplus",
        "ntfs", "ntfs3", "exfat", "vfat", "f2fs", "jfs", "reiserfs", "nilfs2",
        "nfs", "nfs4", "cifs", "smb3", "9p", "virtiofs", "fuse", "fuseblk", "udf", "iso9660"
    };
    private readonly Func<string> _readMountInfo;
    private readonly Func<string?> _readDeploymentMetadata;
    private readonly Func<string?>? _appDataDirectory;
    private readonly Func<string, FileAttributes> _readAttributes;
    private readonly TimeProvider _clock;
    private readonly object _snapshotLock = new();
    private readonly Dictionary<string, RootProbe> _rootProbes = new(StringComparer.Ordinal);
    private static readonly TimeSpan ProbeLifetime = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RootListingWait = TimeSpan.FromMilliseconds(100);
    private const int MaximumPendingRootProbes = 32;
    private string? _lastMountInfo;
    private string? _lastMetadata;
    private DateTimeOffset _snapshotReadAt;
    private Snapshot _snapshot = Snapshot.Empty;

    public UserStoragePolicy(Func<string?>? appDataDirectory = null)
        : this(IsRunningInContainer(), appDataDirectory: appDataDirectory) { }

    /// <summary>Explicit environment seams, also used by offline Setup and deterministic tests.</summary>
    public UserStoragePolicy(bool isContainer, Func<string>? readMountInfo = null,
        Func<string?>? appDataDirectory = null, Func<string?>? readDeploymentMetadata = null,
        Func<string, FileAttributes>? readAttributes = null, TimeProvider? timeProvider = null)
    {
        IsRestricted = isContainer;
        _appDataDirectory = appDataDirectory;
        _readMountInfo = readMountInfo ?? (() => File.ReadAllText("/proc/self/mountinfo"));
        _readDeploymentMetadata = readDeploymentMetadata ??
            (() => Environment.GetEnvironmentVariable(StorageVolumes.DeploymentMountsVariable));
        _readAttributes = readAttributes ?? File.GetAttributes;
        _clock = timeProvider ?? TimeProvider.System;
    }

    public bool IsRestricted { get; }

    public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles)
    {
        if (!IsRestricted) return NativeRoots();
        var snapshot = ReadSnapshot();
        if (!TryAppDataBoundary(purpose, out var appData, out var realAppData)) return [];
        var candidates = snapshot.Mounts.Where(m => m.Allowed &&
                !snapshot.AmbiguousPaths.Any(p => Within(m.Path, p) || Within(m.ResolvedPath, p)) &&
                (appData == null || !Within(m.Path, appData) && !Within(m.ResolvedPath, realAppData!)))
            .ToArray();
        // A disconnected network mount must not hold up the entire storage chooser.
        // Keep at most one probe per root, bound outstanding work and wait once for
        // the whole set. A late/failed root stays unavailable until a later probe.
        var probes = GetRootProbes(candidates);
        try { Task.WaitAll(probes.Values.Distinct().ToArray(), RootListingWait); }
        catch (AggregateException) { /* A failed probe never authorizes storage. */ }
        return candidates.Where(m => probes.TryGetValue(m, out var probe) &&
                probe.IsCompletedSuccessfully && probe.Result)
            .Select(m => new UserStorageRoot(m.Path, Path.GetFileName(m.Path), m.Kind, m.ReadOnly))
            .OrderBy(r => r.Path, StringComparer.Ordinal).ToArray();
    }

    public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles)
    {
        if (!IsRestricted) return true;
        try
        {
            if (!TryAbsolute(path, out var lexical) || !TryResolve(path, out var resolved) ||
                !TryAppDataBoundary(purpose, out var appData, out var realAppData)) return false;
            if (appData != null && (Within(lexical!, appData) || Within(resolved!, realAppData!))) return false;
            var snapshot = ReadSnapshot();
            // Both matter: a symlink cannot leave a persistent mount, and a symlink
            // inside tmpfs/rootfs cannot turn that temporary entry into user storage.
            var lexicalMount = AllowedMount(snapshot, lexical!, canonical: false);
            var resolvedMount = AllowedMount(snapshot, resolved!, canonical: true);
            // Probe only the mounts this path actually uses. In particular, checking
            // an SSD file must never stat unrelated CIFS/NFS roots.
            return lexicalMount != null && resolvedMount != null &&
                   IsDirectoryMount(lexicalMount) &&
                   (ReferenceEquals(lexicalMount, resolvedMount) || IsDirectoryMount(resolvedMount));
        }
        catch (Exception e) when (IsPathError(e)) { return false; }
    }

    public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles)
    {
        if (IsPathAllowed(path, purpose)) return;
        if (TryAbsolute(path, out var lexical) && TryAppDataBoundary(purpose, out var appData, out _) &&
            (appData == null || !Within(lexical!, appData)) &&
            AllowedMount(ReadSnapshot(), lexical!, canonical: false) is { } mount)
        {
            // mountinfo can still list a disconnected SMB/NFS share. Distinguish an
            // inaccessible mount root from an untrusted child or symlink on a healthy mount.
            try { _ = _readAttributes(mount.Path); }
            catch (IOException)
            {
                throw new UserStoragePathException($"Reconnect the mounted storage location. {mount.Path}", mountUnavailable: true);
            }
        }
        throw new UserStoragePathException($"Choose a folder inside a mounted storage location. {path}");
    }

    public void EnsureTreeMutationAllowed(string path)
    {
        if (!IsRestricted) return;
        EnsurePathAllowed(path);
        if (!TryAbsolute(path, out var lexical) || !TryResolve(path, out var resolved) ||
            !TryAppDataBoundary(UserStoragePurpose.UserFiles, out var appData, out var realAppData) ||
            Within(appData!, lexical!) || Within(realAppData!, resolved!))
            throw new UserStoragePathException($"The application data directory and its parent folders cannot be moved, renamed or deleted. {path}");
    }

    private bool TryAppDataBoundary(UserStoragePurpose purpose, out string? lexical, out string? resolved)
    {
        lexical = resolved = null;
        if (purpose == UserStoragePurpose.Setup) return true;
        try
        {
            var path = _appDataDirectory?.Invoke();
            // Only the host's effective-directory resolver knows redirect/import state.
            // Missing that fact must not expose application data as user storage.
            return TryAbsolute(path, out lexical) && TryResolve(path!, out resolved);
        }
        catch (Exception e) when (IsPathError(e)) { return false; }
    }

    private Snapshot ReadSnapshot()
    {
        string text;
        string? metadata;
        try
        {
            text = _readMountInfo();
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumMountInfoLength) return Snapshot.Empty;
            try { metadata = _readDeploymentMetadata(); }
            catch { metadata = null; } // Optional labels never authorize a path.
        }
        catch (Exception e) when (IsPathError(e)) { return Snapshot.Empty; }
        lock (_snapshotLock)
        {
            var now = _clock.GetUtcNow();
            if (text == _lastMountInfo && metadata == _lastMetadata && now - _snapshotReadAt < ProbeLifetime)
                return _snapshot;
            _lastMountInfo = text;
            _lastMetadata = metadata;
            _snapshotReadAt = now;
            return _snapshot = ParseMountInfo(text, metadata);
        }
    }

    private Snapshot ParseMountInfo(string text, string? metadata)
    {
        try
        {
            var kinds = ReadMountKinds(metadata);
            var entries = new List<(string Path, bool Persistent, string Kind, bool ReadOnly)>();
            var ids = new HashSet<int>();
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var separator = Array.IndexOf(fields, "-");
                if (separator < 6 || fields.Length != separator + 4 ||
                    !int.TryParse(fields[0], out var id) || id <= 0 || !ids.Add(id) ||
                    !int.TryParse(fields[1], out var parentId) || parentId < 0 ||
                    !Regex.IsMatch(fields[2], @"\A\d+:\d+\z") ||
                    !TryAbsolute(UnescapeMountField(fields[3]), out _) ||
                    !TryAbsolute(UnescapeMountField(fields[4]), out var original)) return Snapshot.Empty;
                var options = fields[5].Split(',');
                if (!options.Contains("ro") && !options.Contains("rw")) return Snapshot.Empty;
                var filesystem = fields[separator + 1];
                var persistent = PersistentFileSystems.Contains(filesystem) || filesystem.StartsWith("fuse.", StringComparison.Ordinal);
                var readOnly = options.Contains("ro") || fields[separator + 3].Split(',').Contains("ro");
                var kind = kinds.GetValueOrDefault(original!, "mount");
                entries.Add((original!, persistent, kind, readOnly));
                if (entries.Count > 16384) return Snapshot.Empty;
            }
            if (!entries.Any(m => m.Path == "/")) return Snapshot.Empty;
            var mounts = new List<Mount>();
            foreach (var entry in entries.OrderBy(m => m.Path.Length))
            {
                // mountinfo describes the kernel namespace, including nested mounts.
                // Resolve aliases above its first boundary, never probe inside another
                // mount while discovering this one. The target's live resolution and
                // directory type are still checked before authorizing any operation.
                var parent = mounts.Where(m => m.Path != "/" && m.Path != entry.Path && Within(entry.Path, m.Path))
                    .MaxBy(m => m.Path.Length);
                string? canonical;
                var resolved = true;
                if (entry.Path == "/") canonical = "/";
                else if (parent != null)
                {
                    canonical = parent.ResolvedPath + entry.Path[parent.Path.Length..];
                    resolved = parent.NamespaceResolved;
                }
                else
                {
                    resolved = TryResolve(Path.GetDirectoryName(entry.Path)!, out var parentPath);
                    canonical = resolved ? Path.Combine(parentPath!, Path.GetFileName(entry.Path)) : entry.Path;
                }
                var allowed = resolved && entry.Persistent && canonical != "/" &&
                    !SystemDirectories.Any(p => Within(entry.Path, p) || Within(canonical!, p));
                mounts.Add(new(entry.Path, canonical!, allowed, entry.Kind, entry.ReadOnly, resolved));
            }
            // A stacked mount can hide descendants listed for the underlying mount.
            // Without proving which layer is visible, refuse that entire subtree.
            var ambiguous = mounts.GroupBy(m => m.Path, StringComparer.Ordinal).Where(g => g.Count() > 1)
                .Select(g => g.Key).Concat(mounts.GroupBy(m => m.ResolvedPath, StringComparer.Ordinal)
                    .Where(g => g.Count() > 1).Select(g => g.Key)).Distinct(StringComparer.Ordinal).ToArray();
            return new(mounts.OrderByDescending(m => m.Path.Length).ToArray(), ambiguous);
        }
        catch (Exception e) when (IsPathError(e)) { return Snapshot.Empty; }
    }

    private static Dictionary<string, string> ReadMountKinds(string? metadata)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (metadata is not {Length: > 0 and <= 65536}) return result;
        try
        {
            using var document = JsonDocument.Parse(metadata);
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1) return result;
            var mounts = root.GetProperty("mounts");
            if (mounts.GetArrayLength() > 256) return result;
            foreach (var mount in mounts.EnumerateArray())
            {
                if (!TryAbsolute(mount.GetProperty("target").GetString(), out var target)) return [];
                var kind = mount.GetProperty("type").GetString();
                if (!result.TryAdd(target!, kind is "bind" or "volume" ? kind : "mount")) return [];
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { return []; }
        return result;
    }

    private static Mount? AllowedMount(Snapshot snapshot, string path, bool canonical)
    {
        if (SystemDirectories.Any(p => Within(path, p)) || snapshot.AmbiguousPaths.Any(p => Within(path, p))) return null;
        Mount? deepest = null;
        var length = -1;
        foreach (var mount in snapshot.Mounts)
        {
            var root = canonical ? mount.ResolvedPath : mount.Path;
            if (root.Length > length && Within(path, root)) { deepest = mount; length = root.Length; }
        }
        return deepest is {Allowed: true} ? deepest : null;
    }

    private bool IsDirectoryMount(Mount mount)
    {
        try
        {
            return TryResolve(mount.Path, out var canonical) && canonical == mount.ResolvedPath &&
                   (_readAttributes(canonical!) & FileAttributes.Directory) != 0;
        }
        catch (Exception e) when (IsPathError(e)) { return false; }
    }

    private Dictionary<Mount, Task<bool>> GetRootProbes(IReadOnlyList<Mount> mounts)
    {
        lock (_snapshotLock)
        {
            var now = _clock.GetUtcNow();
            var keys = mounts.Select(m => m.Path + "\0" + m.ResolvedPath).ToHashSet(StringComparer.Ordinal);
            foreach (var key in _rootProbes.Where(p => p.Value.Task.IsCompleted && !keys.Contains(p.Key))
                         .Select(p => p.Key).ToArray()) _rootProbes.Remove(key);
            var pending = _rootProbes.Values.Count(p => !p.Task.IsCompleted);
            var result = new Dictionary<Mount, Task<bool>>();
            foreach (var mount in mounts)
            {
                var key = mount.Path + "\0" + mount.ResolvedPath;
                _rootProbes.TryGetValue(key, out var probe);
                if ((probe == null || probe.Task.IsCompleted && now - probe.StartedAt >= ProbeLifetime) &&
                    pending < MaximumPendingRootProbes)
                {
                    probe = new(Task.Run(() => IsDirectoryMount(mount)), now);
                    _rootProbes[key] = probe;
                    pending++;
                }
                if (probe != null) result[mount] = probe.Task;
            }
            return result;
        }
    }

    private static bool Within(string path, string root) => path.Equals(root, StringComparison.Ordinal) ||
        path.StartsWith(root == "/" ? root : root + "/", StringComparison.Ordinal);

    private static bool TryAbsolute(string? path, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrEmpty(path) || path.Length > 32768 || path[0] != '/' || path.Contains('\0')) return false;
        try
        {
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return true;
        }
        catch (Exception e) when (IsPathError(e)) { return false; }
    }

    private bool TryResolve(string path, out string? resolved)
    {
        resolved = null;
        if (!TryAbsolute(path, out _)) return false;
        var remaining = new LinkedList<string>(path.Split('/', StringSplitOptions.RemoveEmptyEntries));
        var current = "/";
        var links = 0;
        var segments = 0;
        try
        {
            while (remaining.First is { } item)
            {
                if (++segments > 1024) return false;
                remaining.RemoveFirst();
                if (item.Value == ".") continue;
                if (item.Value == "..") { current = Path.GetDirectoryName(current) ?? "/"; continue; }
                var next = Path.Combine(current, item.Value);
                FileAttributes attributes;
                try { attributes = _readAttributes(next); }
                catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
                {
                    current = next; // A new target still resolves every existing ancestor.
                    continue;
                }
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    if (++links > 40) return false;
                    FileSystemInfo entry = (attributes & FileAttributes.Directory) != 0
                        ? new DirectoryInfo(next) : new FileInfo(next);
                    var target = entry.LinkTarget;
                    if (string.IsNullOrEmpty(target)) return false;
                    if (target.StartsWith('/')) current = "/";
                    var targetParts = target.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    for (var i = targetParts.Length - 1; i >= 0; i--)
                        remaining.AddFirst(targetParts[i]);
                    continue;
                }
                if ((attributes & FileAttributes.Directory) == 0 && remaining.Count > 0) return false;
                current = next;
            }
            resolved = Path.TrimEndingDirectorySeparator(current);
            return true;
        }
        catch (Exception e) when (IsPathError(e)) { return false; }
    }

    private static string UnescapeMountField(string value) => Regex.Replace(value, @"\\([0-7]{3})",
        match => ((char)Convert.ToInt32(match.Groups[1].Value, 8)).ToString());

    private static bool IsPathError(Exception error) => error is IOException or UnauthorizedAccessException or
        ArgumentException or NotSupportedException or System.Security.SecurityException;

    private static bool IsRunningInContainer() =>
        string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase) ||
        OperatingSystem.IsLinux() && (File.Exists("/.dockerenv") || File.Exists("/run/.containerenv"));

    private static IReadOnlyList<UserStorageRoot> NativeRoots()
    {
        var result = new List<UserStorageRoot>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady) result.Add(new(drive.RootDirectory.FullName,
                    string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name : drive.VolumeLabel, "drive", null));
            }
            catch (Exception e) when (IsPathError(e)) { }
        }
        return result;
    }

    private sealed record RootProbe(Task<bool> Task, DateTimeOffset StartedAt);
    private sealed record Mount(string Path, string ResolvedPath, bool Allowed, string Kind, bool ReadOnly,
        bool NamespaceResolved);
    private sealed record Snapshot(IReadOnlyList<Mount> Mounts, IReadOnlyList<string> AmbiguousPaths)
    {
        public static readonly Snapshot Empty = new([], []);
    }
}
