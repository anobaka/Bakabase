using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.Service.Components.FileProcessing;

internal sealed record DeferredDirectoryIdentity(string Path, long Device, long Inode, Guid? CleanupIdentity = null);

internal enum DeferredEmptyDirectoryResult { Completed, Waiting, Changed }

/// <summary>Reclaims empty directories after the SMB client releases its own delete-on-close files.</summary>
internal static partial class DeferredEmptyDirectoryTree
{
    private const int MaxDirectories = 2048, MaxDepth = 64, MaxEntries = 32768, MaxRelativeLength = 4096;
    private sealed class ChangedTree : Exception;
    private sealed class PendingTree : Exception;
    private sealed class Snapshot
    {
        internal readonly Dictionary<string, DeferredDirectoryIdentity> Directories = new(StringComparer.Ordinal);
        // Only a running extraction's independently pinned, private directory can opt in.
        internal Dictionary<string, RegularEntry>? ExtractionFiles;
        internal long? MountId;
        internal bool Markers, OtherRegularFiles;
        internal int Entries;
    }

    internal static IReadOnlyList<DeferredDirectoryIdentity>? CaptureMarkerTree(string root, CancellationToken ct = default)
    {
        var first = Capture(root, null, ct);
        return first is { Markers: true, OtherRegularFiles: false } ? BindConfirmedTree(root, first, ct) : null;
    }

    internal static async Task<IReadOnlyList<DeferredDirectoryIdentity>?> CaptureAfterDeleteAsync(string root, CancellationToken ct = default)
    {
        var first = Capture(root, null, ct);
        if (first is not { Markers: true }) return null;
        // Some clients briefly expose old regular names alongside delete-on-close files.
        if (first.OtherRegularFiles) await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
        return BindConfirmedTree(root, first, ct);
    }

    private static IReadOnlyList<DeferredDirectoryIdentity>? BindConfirmedTree(string root, Snapshot first, CancellationToken ct)
    {
        // Reopen only directories observed in the user's deletion, then attach the durable
        // identity to each verified descriptor. A subsequent worker never creates an identity.
        var bound = Capture(root, first.Directories, ct, bind: true);
        return bound is { Directories.Count: > 0 }
            ? bound.Directories.Values.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray()
            : null;
    }

    private static Snapshot? Capture(string root, Dictionary<string, DeferredDirectoryIdentity>? approved, CancellationToken ct, bool bind = false)
    {
        ct.ThrowIfCancellationRequested();
        if (!Supported) return null;
        var snapshot = new Snapshot();
        try
        {
            WithParent(root, (parent, leaf) => Inspect(parent, leaf, "", 0, snapshot, approved, ct, bind));
            return snapshot;
        }
        catch (Exception e) when (e is ChangedTree or PendingTree) { return null; }
    }

    internal static DeferredEmptyDirectoryResult InspectEmptyTree(string root, IReadOnlyList<DeferredDirectoryIdentity> confirmedRelativeDirectories,
        CancellationToken ct = default) => CheckTree(root, confirmedRelativeDirectories, false, ct);

    internal static DeferredEmptyDirectoryResult TryRemoveEmptyTree(string root, IReadOnlyList<DeferredDirectoryIdentity> confirmedRelativeDirectories,
        CancellationToken ct = default) => CheckTree(root, confirmedRelativeDirectories, true, ct);

    private static DeferredEmptyDirectoryResult CheckTree(string root, IReadOnlyList<DeferredDirectoryIdentity> confirmedRelativeDirectories,
        bool remove, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!ValidReceipt(confirmedRelativeDirectories, out var approved)) return DeferredEmptyDirectoryResult.Changed;
        if (!Supported) return DeferredEmptyDirectoryResult.Waiting;
        try
        {
            WithParent(root, (parent, leaf) =>
            {
                var snapshot = new Snapshot();
                Inspect(parent, leaf, "", 0, snapshot, approved, ct);
                if (snapshot.Markers) throw new PendingTree();
                // Check the complete tree before removing even its first empty sibling. Reopen
                // each name against its pinned parent, verifying the object from the first pass.
                if (remove && snapshot.Directories.Count > 0) Remove(parent, leaf, "", snapshot, ct);
            });
            return DeferredEmptyDirectoryResult.Completed;
        }
        catch (ChangedTree) { return DeferredEmptyDirectoryResult.Changed; }
        catch (PendingTree) { return DeferredEmptyDirectoryResult.Waiting; }
    }

    // Other architectures use different openat flags. Unsupported hosts keep their manual
    // retry; a path-based recursive delete cannot provide this operation's guarantees.
    private static bool Supported => OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() &&
        RuntimeInformation.ProcessArchitecture is Architecture.Arm or Architecture.Arm64 or Architecture.X64 or Architecture.X86;
    private static int OpenDirectoryFlags => OperatingSystem.IsMacOS() ? 0x01000000 | 0x00100000 | 0x00000100 :
        (1 << 19) | (RuntimeInformation.ProcessArchitecture is Architecture.Arm or Architecture.Arm64 ?
            (1 << 14) | (1 << 15) : (1 << 16) | (1 << 17)); // O_CLOEXEC | O_DIRECTORY | O_NOFOLLOW

    private static bool SafeName(string s) => s.Length > 0 && s is not "." and not ".." &&
        s.IndexOfAny(['/', '\\', '\0', '\uFFFD']) < 0;

    private static bool ValidReceipt(IReadOnlyList<DeferredDirectoryIdentity>? entries, out Dictionary<string, DeferredDirectoryIdentity> approved)
    {
        approved = new(StringComparer.Ordinal);
        if (entries is not {Count: > 0 and <= MaxDirectories}) return false;
        foreach (var entry in entries)
        {
            if (entry?.Path is not {} name || entry.Inode == 0 || name.Length > MaxRelativeLength ||
                entry.CleanupIdentity == Guid.Empty || !approved.TryAdd(name, entry)) return false;
            if (name.Length == 0) continue;
            var parts = name.Split('/');
            if (parts.Length > MaxDepth || !parts.All(SafeName)) return false;
        }
        if (!approved.ContainsKey("")) return false;
        foreach (var name in approved.Keys)
        {
            var slash = name.LastIndexOf('/');
            if (slash >= 0 && !approved.ContainsKey(name[..slash])) return false;
        }
        return true;
    }

    private static void WithParent(string root, Action<int, string> action)
    {
        if (string.IsNullOrWhiteSpace(root) || !root.StartsWith('/') || root.Length > MaxRelativeLength) throw new ChangedTree();
        var parts = root.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !parts.All(SafeName)) throw new ChangedTree();
        var fd = OpenDirectory(-1, "/");
        if (fd < 0) throw new PendingTree();
        try
        {
            foreach (var part in parts[..^1])
            {
                var next = OpenDirectory(fd, part);
                if (next < 0) return; // A whole ancestor is already absent.
                Close(fd);
                fd = next;
            }
            action(fd, parts[^1]);
        }
        finally { Close(fd); }
    }

    private static int OpenDirectory(int parent, string name)
    {
        var fd = OpenAt(parent, name, OpenDirectoryFlags);
        if (fd >= 0) return fd;
        var error = Marshal.GetLastPInvokeError();
        if (error == 2) return -1; // ENOENT
        if (error == 20 || error == (OperatingSystem.IsMacOS() ? 62 : 40)) throw new ChangedTree(); // ENOTDIR, ELOOP
        throw new PendingTree();
    }

    private static void Inspect(int parent, string name, string relative, int depth, Snapshot state,
        Dictionary<string, DeferredDirectoryIdentity>? approved, CancellationToken ct, bool bind = false)
    {
        ct.ThrowIfCancellationRequested();
        if (depth > MaxDepth || relative.Length > MaxRelativeLength || state.Directories.Count >= MaxDirectories ||
            approved != null && !approved.ContainsKey(relative)) throw new ChangedTree();
        var fd = OpenDirectory(parent, name);
        if (fd < 0) return;
        try
        {
            var (device, inode) = Identity(fd);
            if (state.ExtractionFiles != null && MountId(fd) != state.MountId) throw new ChangedTree();
            if (relative.Length > 0 && device != state.Directories[""].Device) throw new ChangedTree();
            if (approved != null)
            {
                var expected = approved[relative];
                if (expected.CleanupIdentity is {} token)
                {
                    // Long-lived SMB mounts may renumber every inode while still serving the
                    // same directories. The per-directory attribute survives that renumbering.
                    if (DirectoryCleanupIdentity.Read(fd) != token) throw new ChangedTree();
                }
                else if (expected.Device != device || expected.Inode != inode) throw new ChangedTree();
            }
            var identity = new DeferredDirectoryIdentity(relative, device, inode, bind ? DirectoryCleanupIdentity.GetOrCreate(fd) : null);
            if (!state.Directories.TryAdd(relative, identity)) throw new ChangedTree();
            foreach (var (child, kind) in Entries(fd))
            {
                ct.ThrowIfCancellationRequested();
                if (++state.Entries > MaxEntries) throw new ChangedTree();
                if (kind == 4) Inspect(fd, child, Child(relative, child), depth + 1, state, approved, ct, bind); // DT_DIR
                else if (kind == 8 && FileProcessingFiles.IsSmbDeleteMarker(child)) state.Markers = true; // DT_REG
                else if (kind == 8 && state.ExtractionFiles is {} files)
                {
                    var fact = ReadRegular(fd, child, state.MountId);
                    if (fact.Device != device || !files.TryAdd(Child(relative, child), fact)) throw new ChangedTree();
                }
                else if (kind == 8 && approved == null) state.OtherRegularFiles = true;
                else throw new ChangedTree(); // Includes symlinks, special files and unknown SMB entry types.
            }
        }
        finally { Close(fd); }
    }

    private static void Remove(int parent, string name, string relative, Snapshot snapshot, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!snapshot.Directories.TryGetValue(relative, out var identity)) throw new ChangedTree();
        var fd = OpenDirectory(parent, name);
        if (fd < 0) return;
        try
        {
            if (Identity(fd) != (identity.Device, identity.Inode) ||
                snapshot.ExtractionFiles != null && MountId(fd) != snapshot.MountId) throw new ChangedTree();
            foreach (var (child, kind) in Entries(fd))
            {
                ct.ThrowIfCancellationRequested();
                if (kind == 4) Remove(fd, child, Child(relative, child), snapshot, ct);
                else if (kind == 8 && FileProcessingFiles.IsSmbDeleteMarker(child)) throw new PendingTree();
                else if (kind == 8 && snapshot.ExtractionFiles?.TryGetValue(Child(relative, child), out var expected) == true)
                {
                    if (ReadRegular(fd, child, snapshot.MountId) != expected) throw new ChangedTree();
                    if (UnlinkAt(fd, child, 0) != 0) throw new PendingTree();
                }
                else throw new ChangedTree();
            }
            var named = OpenDirectory(parent, name);
            if (named < 0) return;
            try
            {
                if (Identity(named) != (identity.Device, identity.Inode)) throw new ChangedTree();
                ct.ThrowIfCancellationRequested();
                // AT_REMOVEDIR cannot unlink a regular file or symlink even if one races
                // into this name. The kernel also refuses directories which acquired a child.
                if (UnlinkAt(parent, name, OperatingSystem.IsMacOS() ? 0x80 : 0x200) == 0) return;
                var error = Marshal.GetLastPInvokeError();
                if (error == 2) return;
                if (error == 20) throw new ChangedTree();
                if (error == 17 || error == (OperatingSystem.IsMacOS() ? 66 : 39)) // EEXIST, ENOTEMPTY
                    foreach (var (entry, kind) in Entries(fd))
                        if (kind != 8 || !FileProcessingFiles.IsSmbDeleteMarker(entry)) throw new ChangedTree();
                throw new PendingTree();
            }
            finally { Close(named); }
        }
        finally { Close(fd); }
    }

    private static string Child(string parent, string name) => parent.Length == 0 ? name : parent + "/" + name;

    private static IEnumerable<(string Name, int Kind)> Entries(int fd)
    {
        // fdopendir owns the second file description; its directory offset is independent
        // of the caller. System.Native provides the portable Darwin/Linux dirent layout.
        var copy = OpenDirectory(fd, ".");
        if (copy < 0) throw new PendingTree();
        var stream = FdOpenDir(copy);
        if (stream == IntPtr.Zero) { Close(copy); throw new PendingTree(); }
        var buffer = IntPtr.Zero;
        try
        {
            // .NET 10 owns its readdir buffer inside the directory stream. Earlier
            // runtimes need a caller-owned buffer for the reentrant entry point.
            var usesStreamBuffer = Environment.Version.Major >= 10;
            var size = usesStreamBuffer ? 0 : GetBufferSize();
            if (!usesStreamBuffer) buffer = Marshal.AllocHGlobal(Math.Max(1, size));
            while (true)
            {
                var result = usesStreamBuffer ? ReadDir(stream, out var entry) : ReadDirR(stream, buffer, size, out entry);
                if (result == -1) yield break;
                if (result != 0 || entry.Name == IntPtr.Zero) throw new PendingTree();
                var name = entry.NameLength < 0 ? Marshal.PtrToStringUTF8(entry.Name) : Marshal.PtrToStringUTF8(entry.Name, entry.NameLength);
                if (name is "." or "..") continue;
                if (name == null) throw new ChangedTree();
                var safeComponent = SafeName(name);
                // A backslash is a legal Unix leaf character in a Darwin SMB marker.
                // It may only be observed on a regular file, never traversed as a directory.
                if (!safeComponent && !FileProcessingFiles.IsSmbDeleteMarker(name)) throw new ChangedTree();
                var kind = entry.Kind;
                // CIFS and FUSE may return DT_UNKNOWN. O_PATH reads only metadata,
                // including the type of a symlink itself, without opening its target.
                if (kind == 0 && OperatingSystem.IsLinux())
                {
                    var noFollow = RuntimeInformation.ProcessArchitecture is Architecture.Arm or Architecture.Arm64 ? 1 << 15 : 1 << 17;
                    var child = OpenAt(fd, name, (1 << 21) | (1 << 19) | noFollow); // O_PATH | O_CLOEXEC | O_NOFOLLOW
                    if (child < 0)
                    {
                        if (Marshal.GetLastPInvokeError() == 2) continue;
                        throw new PendingTree();
                    }
                    try
                    {
                        if (FStat(child, out var status) != 0) throw new PendingTree();
                        kind = (status.Mode & 0xF000) >> 12; // S_IF* encodes its dirent type.
                    }
                    finally { Close(child); }
                }
                if (!safeComponent && kind != 8) throw new ChangedTree();
                yield return (name, kind);
            }
        }
        finally { Marshal.FreeHGlobal(buffer); CloseDir(stream); }
    }

    // System.Native's FileStatus ABI normalizes the stat layout on all supported Unix hosts.
    [StructLayout(LayoutKind.Sequential)]
    private struct FileStatus
    {
        internal int Flags, Mode;
        internal uint Uid, Gid;
        internal long Size, ATime, ATimeNsec, MTime, MTimeNsec, CTime, CTimeNsec, BirthTime, BirthTimeNsec, Dev, RDev, Ino;
        internal uint UserFlags;
    }
    private static (long Device, long Inode) Identity(int fd) =>
        FStat(fd, out var s) == 0 && s.Ino != 0 && (s.Mode & 0xF000) == 0x4000 ? (s.Dev, s.Ino) : throw new PendingTree();

    [StructLayout(LayoutKind.Sequential)]
    private struct DirectoryEntry { internal IntPtr Name; internal int NameLength, Kind; }
    [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
    private static extern int OpenAt(int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags);
    [DllImport("libc", EntryPoint = "unlinkat", SetLastError = true)]
    private static extern int UnlinkAt(int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags);
    [DllImport("libc", EntryPoint = "close")]
    private static extern int Close(int fd);
    [DllImport("libc", EntryPoint = "fdopendir")]
    private static extern IntPtr FdOpenDir(int fd);
    [DllImport("libc", EntryPoint = "closedir")]
    private static extern int CloseDir(IntPtr dir);
    [DllImport("System.Native", EntryPoint = "SystemNative_GetReadDirRBufferSize")]
    private static extern int GetBufferSize();
    [DllImport("System.Native", EntryPoint = "SystemNative_ReadDirR")]
    private static extern int ReadDirR(IntPtr dir, IntPtr buffer, int size, out DirectoryEntry entry);
    [DllImport("System.Native", EntryPoint = "SystemNative_ReadDir")]
    private static extern int ReadDir(IntPtr dir, out DirectoryEntry entry);
    [DllImport("System.Native", EntryPoint = "SystemNative_FStat")]
    private static extern int FStat(nint fd, out FileStatus status);
}
