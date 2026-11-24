using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Bakabase.Abstractions.Components.FileSystem;

/// <summary>Checks regular files by the opened filesystem object and its current name.</summary>
public static class RegularFileHandle
{
    // System.Native unifies Darwin and Linux stat layouts in .NET 9/10 (pal_io.h).
    [StructLayout(LayoutKind.Sequential)]
    private struct UnixFileStatus
    {
        internal int Flags, Mode;
        internal uint Uid, Gid;
        internal long Size, AccessTime, AccessTimeNsec, ModificationTime, ModificationTimeNsec,
            ChangeTime, ChangeTimeNsec, BirthTime, BirthTimeNsec, Device, SpecialDevice, Inode;
        internal uint UserFlags;
    }

    [DllImport("System.Native", EntryPoint = "SystemNative_FStat")]
    private static extern int FStat(SafeFileHandle handle, out UnixFileStatus status);

    [DllImport("System.Native", EntryPoint = "SystemNative_LStat")]
    private static extern int LStat([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out UnixFileStatus status);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsFileInformation
    {
        internal uint Attributes;
        internal System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        internal uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out WindowsFileInformation info);

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    /// <summary>Opens the exact leaf, even if a directory listing has stale attributes. On Unix a
    /// substituted pipe must not block and a link must not be followed. Null means the leaf is absent.</summary>
    public static FileStream? TryOpenRead(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                RequireRegularFile(path);
                var opened = File.OpenRead(path);
                try { RequireRegularFile(path); return opened; }
                catch { opened.Dispose(); throw; }
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }
        // O_RDONLY | O_NONBLOCK | O_NOFOLLOW | O_CLOEXEC on Darwin and Linux.
        var flags = OperatingSystem.IsMacOS() ? 0x4 | 0x100 | 0x1000000 : 0x800 | 0x20000 | 0x80000;
        var descriptor = Open(path, flags);
        if (descriptor < 0)
        {
            int error = Marshal.GetLastPInvokeError();
            if (error == 2) return null; // ENOENT
            throw new IOException($"Opening a regular file failed (errno {error}).");
        }
        var handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
        try
        {
            if (FStat(handle, out var file) != 0 || (file.Mode & 0xF000) != 0x8000)
                throw new IOException("The opened input is not a regular file.");
            return new FileStream(handle, FileAccess.Read, bufferSize: 1);
        }
        catch { handle.Dispose(); throw; }
    }

    /// <summary>Reject special files before opening a user-provided payload.</summary>
    public static void RequireRegularFile(string path)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            if (LStat(path, out var file) != 0 || (file.Mode & 0xF000) != 0x8000)
                throw new InvalidDataException("The input is not a regular file.");
        }
        else if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("The input is not a regular file.");
    }

    /// <summary>A filesystem object identity that remains stable when a regular file moves on the same volume.</summary>
    public static string Identity(FileStream stream, string path)
    {
        if (OperatingSystem.IsWindows())
        {
            if (GetFileInformationByHandle(stream.SafeFileHandle, out var status) &&
                (status.Attributes & (uint)(FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0 &&
                (status.FileIndexHigh != 0 || status.FileIndexLow != 0))
                return $"{status.VolumeSerialNumber:X8}:{status.FileIndexHigh:X8}{status.FileIndexLow:X8}";
        }
        else if (FStat(stream.SafeFileHandle, out var status) == 0 && status.Inode != 0 &&
                 (status.Mode & 0xF000) == 0x8000 && MatchesPath(stream, path) == true)
        {
            return $"{status.Device:X}:{status.Inode:X}";
        }
        throw new IOException("A stable file identity is unavailable.");
    }

    public static string? ReadContentGeneration(FileStream stream, string path, bool requireNamedChangeTime = true)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return null;
        if (FStat(stream.SafeFileHandle, out var file) != 0 || LStat(path, out var named) != 0 ||
            (file.Mode & 0xF000) != 0x8000 || file.Inode == 0 || file.Device != named.Device ||
            (named.Mode & 0xF000) != 0x8000 || file.Inode != named.Inode ||
            (requireNamedChangeTime && (file.ChangeTime != named.ChangeTime || file.ChangeTimeNsec != named.ChangeTimeNsec)))
            throw new IOException("The file changed during verification.");
        return $"{file.Device:X}:{file.Inode:X}:{file.Size:X}:{file.ChangeTime:X}:{file.ChangeTimeNsec:X}:{file.ModificationTime:X}:{file.ModificationTimeNsec:X}";
    }

    /// <summary>The file name must still identify the opened file. SMB name metadata can lag behind an
    /// exact handle after a rename, so take the size and content timestamps from the handle.</summary>
    public static string ReadVersion(FileStream stream, string path)
    {
        if (ReadContentGeneration(stream, path, requireNamedChangeTime: false) is { } unix) return unix;
        // The reader opened with FileShare.Read, preventing writes and replacement on Windows.
        return $"{Identity(stream, path)}:{RandomAccess.GetLength(stream.SafeFileHandle):X}:{File.GetLastWriteTimeUtc(stream.SafeFileHandle).Ticks:X}";
    }

    public static bool? MatchesPath(FileStream stream, string path)
    {
        if (FStat(stream.SafeFileHandle, out var file) != 0 || LStat(path, out var named) != 0 ||
            file.Inode == 0 || named.Inode == 0) return null;
        return (named.Mode & 0xF000) == 0x8000 && file.Device == named.Device && file.Inode == named.Inode;
    }

    public static bool SameRegularFile(FileStream first, FileStream second, string path) =>
        FStat(first.SafeFileHandle, out var a) == 0 && FStat(second.SafeFileHandle, out var b) == 0 &&
        a.Inode != 0 && a.Device == b.Device && a.Inode == b.Inode && MatchesPath(second, path) == true;
}
