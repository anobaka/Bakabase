using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Bakabase.Modules.Downloader.Components;

/// <summary>Never open a pipe as transfer data or mutate another file through a hard link.
/// The checkpoint root itself is private to the provider and has already been checked for reparse points.</summary>
internal static class AuthorizedOriginalCheckpointFile
{
    internal static void RequirePrivateRegularFile(string path, bool isLock)
    {
        if (OperatingSystem.IsMacOS())
        {
            if (MacLStat(path, out var file) != 0 || (file.Mode & 0xf000) != 0x8000 || file.Links != 1)
                throw AuthorizedOriginalCheckpointStore.Invalid();
        }
        else if (OperatingSystem.IsLinux())
        {
            const uint typeAndLinkCount = 1 | 4;
            if (LinuxStatx(-100, path, 0x100, typeAndLinkCount, out var file) != 0 ||
                (file.Mask & typeAndLinkCount) != typeAndLinkCount || (file.Mode & 0xf000) != 0x8000 || file.Links != 1)
                throw AuthorizedOriginalCheckpointStore.Invalid();
        }
        else if (OperatingSystem.IsWindows() && !isLock)
        {
            // The empty lock is never written and cannot be reopened while leased on Windows.
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!WinFileInfo(handle, out var info) || info.Links != 1)
                throw AuthorizedOriginalCheckpointStore.Invalid();
        }
    }

    // Darwin's 64-bit stat ABI (the arm64 symbol is unversioned).
    [StructLayout(LayoutKind.Explicit, Size = 144)]
    private struct MacStat
    {
        [FieldOffset(4)] public ushort Mode;
        [FieldOffset(6)] public ushort Links;
    }
    private static int MacLStat(string path, out MacStat file) => RuntimeInformation.ProcessArchitecture == Architecture.Arm64
        ? DarwinLStat(path, out file) : DarwinLStat64(path, out file);
    [DllImport("libc", EntryPoint = "lstat")]
    private static extern int DarwinLStat([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out MacStat file);
    [DllImport("libc", EntryPoint = "lstat$INODE64")]
    private static extern int DarwinLStat64([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out MacStat file);

    // Linux statx is the same layout on all architectures.
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxStat
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(16)] public uint Links;
        [FieldOffset(28)] public ushort Mode;
    }
    [DllImport("libc", EntryPoint = "statx")]
    private static extern int LinuxStatx(int dir, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, out LinuxStat file);

    [StructLayout(LayoutKind.Explicit, Size = 52)]
    private struct WinInfo { [FieldOffset(40)] public uint Links; }
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinFileInfo(SafeFileHandle file, out WinInfo info);
}
