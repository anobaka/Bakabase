using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Bakabase.InsideWorld.Business.Components.Artifacts;

public static class ArtifactFileIdentity
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

    /// <summary>A filesystem object identity that remains stable when a regular file moves on the same volume.</summary>
    public static string Read(string path)
    {
        ArtifactManifestFiles.RejectLinks(path);
        using var stream = File.OpenRead(path);
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

    /// <summary>Tracks content and name changes across phases of a single verified read.</summary>
    public static string? ReadContentGeneration(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return null;
        ArtifactManifestFiles.RejectLinks(path);
        using var stream = File.OpenRead(path);
        if (FStat(stream.SafeFileHandle, out var file) != 0 || LStat(path, out var named) != 0 ||
            (file.Mode & 0xF000) != 0x8000 || file.Inode == 0 || file.Device != named.Device ||
            file.Inode != named.Inode || file.ChangeTime != named.ChangeTime || file.ChangeTimeNsec != named.ChangeTimeNsec)
            throw new IOException("The file changed during verification.");
        return $"{file.Device:X}:{file.Inode:X}:{file.Size:X}:{file.ChangeTime:X}:{file.ChangeTimeNsec:X}:{file.ModificationTime:X}:{file.ModificationTimeNsec:X}";
    }

    internal static bool? MatchesPath(FileStream stream, string path)
    {
        if (FStat(stream.SafeFileHandle, out var file) != 0 || LStat(path, out var named) != 0 ||
            file.Inode == 0 || named.Inode == 0) return null;
        return (named.Mode & 0xF000) == 0x8000 && file.Device == named.Device && file.Inode == named.Inode;
    }

    internal static bool SameRegularFile(FileStream first, FileStream second, string path) =>
        FStat(first.SafeFileHandle, out var a) == 0 && FStat(second.SafeFileHandle, out var b) == 0 &&
        a.Inode != 0 && a.Device == b.Device && a.Inode == b.Inode && MatchesPath(second, path) == true;
}
