using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Bakabase.Abstractions.Components.FileSystem;

public static partial class RegularFileHandle
{
    /// <summary>Version used when a verified result replaces a temporary file. Windows write times can be restored.</summary>
    public static string ReadRetirementVersion(FileStream file, string path)
    {
        var version = ReadVersion(file, path);
        if (!OperatingSystem.IsWindows()) return version;
        const int fileBasicInfo = 0;
        if (!GetFileInformationByHandleEx(file.SafeFileHandle, fileBasicInfo, out var info,
                (uint)Marshal.SizeOf<WindowsBasicFileInformation>()) || info.ChangeTime <= 0)
            throw new IOException("The file change time is unavailable.");
        return $"{version}:{info.ChangeTime:X}";
    }

    /// <summary>Marks only the recorded version of a single-link regular file for deletion.</summary>
    public static bool TryDeleteRecordedVersionOnWindows(string path, string expectedVersion)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(expectedVersion)) return false;
        const uint genericRead = 0x80000000, delete = 0x00010000, openReparsePoint = 0x00200000;
        const int fileDispositionInfo = 4;
        try
        {
            var fullPath = Path.GetFullPath(path);
            using var handle = CreateFileW(fullPath, genericRead | delete, FileShare.Read, IntPtr.Zero,
                FileMode.Open, openReparsePoint, IntPtr.Zero);
            if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info) || info.NumberOfLinks != 1 ||
                (info.Attributes & (uint)(FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0)
                return false;

            using var file = new FileStream(handle, FileAccess.Read, bufferSize: 1);
            if (!string.Equals(ReadRetirementVersion(file, fullPath), expectedVersion, StringComparison.Ordinal)) return false;

            // FILE_DISPOSITION_INFO contains one BOOLEAN. The kernel unlinks this handle's file on close.
            byte deleteFile = 1;
            return SetFileInformationByHandle(handle, fileDispositionInfo, in deleteFile, sizeof(byte));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsBasicFileInformation
    {
        internal long CreationTime, LastAccessTime, LastWriteTime, ChangeTime;
        internal uint FileAttributes;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass,
        out WindowsBasicFileInformation information, uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, FileShare sharing, IntPtr security,
        FileMode disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass,
        in byte deleteFile, uint size);
}
