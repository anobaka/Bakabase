using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Bakabase.Service.Components.FileProcessing;

public sealed record ExtractionStagingIdentity(Guid Token, long Device, long Inode, long? MountId, bool WriterStarted = false);

internal static partial class DeferredEmptyDirectoryTree
{
    private sealed record RegularEntry(long Device, long Inode, long Size, long Modified, long ModifiedNs);

    // A bind mount can have the same device as the parent it hides. Pin its mount ID
    // on Linux as well as the inode. Hosts without a verifiable mount keep the output.
    private static long? MountId(int fd)
    {
        if (!OperatingSystem.IsLinux()) return null;
        foreach (var line in File.ReadLines($"/proc/self/fdinfo/{fd}"))
            if (line.StartsWith("mnt_id:", StringComparison.Ordinal) &&
                long.TryParse(line.AsSpan(7).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id)) return id;
        throw new ChangedTree();
    }

    private static RegularEntry ReadRegular(int parent, string leaf, long? mount, bool requireSingleLink = false)
    {
        var flags = OperatingSystem.IsMacOS() ? 0x01000000 | 0x8000 | 0x100 | 0x4 : // O_CLOEXEC | O_EVTONLY | O_NOFOLLOW | O_NONBLOCK
            (1 << 21) | (OpenDirectoryFlags & ~(RuntimeInformation.ProcessArchitecture is Architecture.Arm or Architecture.Arm64 ? 1 << 14 : 1 << 16));
        var fd = OpenAt(parent, leaf, flags);
        if (fd < 0) throw new ChangedTree();
        try
        {
            if (FStat(fd, out var file) != 0 || file.Ino == 0 || (file.Mode & 0xF000) != 0x8000 || MountId(fd) != mount || requireSingleLink && !SingleLink(fd))
                throw new ChangedTree();
            return new(file.Dev, file.Ino, file.Size, file.MTime, file.MTimeNsec);
        }
        finally { Close(fd); }
    }

    // Unlike Directory.CreateDirectory, mkdir/CREATE_DIRECTORY report an occupied
    // name as a failure. A new attempt must never adopt an existing directory.
    internal static void CreateFreshExtractionDirectory(string root)
    {
        var created = false;
        if (Supported)
            WithParent(root, (parent, leaf) => created = MkDirAt(parent, leaf, 0x1FF) == 0); // 0777, subject to the process umask
        else if (OperatingSystem.IsWindows()) created = CreateWindowsDirectory(root, IntPtr.Zero);
        if (!created) throw new IOException("Could not exclusively create the archive staging directory.");
    }

    [DllImport("libc", EntryPoint = "mkdirat", SetLastError = true)]
    private static extern int MkDirAt(int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, uint mode);

    [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateWindowsDirectory(string name, IntPtr attributes);

    internal sealed class ExtractionLease(string root, int pinned, ExtractionStagingIdentity owner) : IDisposable
    {
        private int _pinned = pinned;
        internal ExtractionStagingIdentity Owner { get; } = owner;
        internal void ConfirmEmpty()
        {
            // Reopen against the pinned directory after a possibly long capacity wait.
            if (!IsEmptyPinned(root, _pinned)) throw new InvalidDataException("The archive staging directory changed before extraction. Its contents were retained.");
        }
        internal void Discard() => DiscardExtraction(root, _pinned, null);
        public void Dispose()
        {
            var fd = Interlocked.Exchange(ref _pinned, -1);
            if (fd >= 0) Close(fd);
        }
    }

    private static bool IsEmptyPinned(string root, int pinned)
    {
        var empty = false;
        try
        {
            WithParent(root, (parent, leaf) =>
            {
                var fd = OpenDirectory(parent, leaf);
                if (fd < 0) return;
                try { empty = Identity(fd) == Identity(pinned) && MountId(fd) == MountId(pinned) && !Entries(fd).Any(); }
                finally { Close(fd); }
            });
        }
        catch (Exception e) when (CannotVerify(e)) { }
        return empty;
    }

    internal static ExtractionLease? PinEmptyExtraction(string root, ExtractionStagingIdentity? expected = null)
    {
        if (!Supported) return null;
        ExtractionLease? lease = null;
        try
        {
            WithParent(root, (parent, leaf) =>
            {
                var fd = OpenDirectory(parent, leaf);
                if (fd < 0) return;
                try
                {
                    var identity = Identity(fd);
                    var mount = MountId(fd);
                    if (Identity(parent).Device != identity.Device || MountId(parent) != mount || Entries(fd).Any()) return;
                    // Some filesystems cannot store xattrs. The open descriptor still pins this
                    // attempt, but a later process without the token will leave the old tree alone.
                    if (expected != null && (identity != (expected.Device, expected.Inode) ||
                        mount != expected.MountId || DirectoryCleanupIdentity.Read(fd) != expected.Token)) return;
                    var token = expected?.Token ?? DirectoryCleanupIdentity.GetOrCreate(fd) ?? Guid.NewGuid();
                    lease = new(root, fd, new(token, identity.Device, identity.Inode, mount));
                    fd = -1;
                }
                finally { if (fd >= 0) Close(fd); }
            });
        }
        catch (Exception e) when (CannotVerify(e)) { }
        return lease;
    }

    internal static bool DiscardClaimedExtraction(string root, ExtractionStagingIdentity owner) => owner.WriterStarted && DiscardExtraction(root, -1, owner);

    private static bool DiscardExtraction(string root, int pinned, ExtractionStagingIdentity? owner)
    {
        if (!Supported || pinned < 0 && owner == null) return false;
        var removed = false;
        try
        {
            WithParent(root, (parent, leaf) =>
            {
                var fd = OpenDirectory(parent, leaf);
                if (fd < 0) return;
                try
                {
                    var identity = Identity(fd);
                    var mount = MountId(fd);
                    if (identity.Device != Identity(parent).Device || mount != MountId(parent) ||
                        (pinned >= 0 ? identity != Identity(pinned) || mount != MountId(pinned) :
                            owner == null || identity != (owner.Device, owner.Inode) || mount != owner.MountId ||
                            DirectoryCleanupIdentity.Read(fd) != owner.Token)) return;
                    var snapshot = new Snapshot {ExtractionFiles = new(StringComparer.Ordinal), MountId = mount};
                    Inspect(parent, leaf, "", 0, snapshot, null, CancellationToken.None);
                    if (snapshot.Markers || !snapshot.Directories.TryGetValue("", out var original) ||
                        (original.Device, original.Inode) != identity) return;
                    Remove(parent, leaf, "", snapshot, CancellationToken.None);
                    removed = true;
                }
                finally { Close(fd); }
            });
        }
        catch (Exception e) when (CannotVerify(e)) { }
        return removed;
    }

    private static bool CannotVerify(Exception error) => error is PendingTree or ChangedTree or IOException or
        UnauthorizedAccessException or PlatformNotSupportedException or DllNotFoundException or EntryPointNotFoundException;
}
