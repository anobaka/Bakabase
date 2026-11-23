using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Bakabase.Service.Components.FileProcessing;

internal static partial class DeferredEmptyDirectoryTree
{
    internal sealed record PrivateFile(long Device, long Inode, long Length, long Modified, long ModifiedNs,
        long ParentDevice, long ParentInode, long? Mount);

    // Use the same no-follow, descriptor-relative walk as extraction cleanup. A regular file
    // with another hard link is deliberately outside an abandoned attempt's exclusive inventory.
    internal static PrivateFile? InspectPrivateFile(string path, PrivateFile? unlink = null)
    {
        if (!Supported) return null;
        PrivateFile? observed = null;
        try
        {
            WithParent(path, (parent, leaf) =>
            {
                var mount = MountId(parent);
                var file = ReadRegular(parent, leaf, mount, requireSingleLink: true);
                var directory = Identity(parent);
                observed = new(file.Device, file.Inode, file.Size, file.Modified, file.ModifiedNs,
                    directory.Device, directory.Inode, mount);
                if (unlink != null && (observed != unlink || UnlinkAt(parent, leaf, 0) != 0)) throw new ChangedTree();
            });
            return observed;
        }
        catch (Exception error) when (CannotVerify(error) || error is EntryPointNotFoundException) { return null; }
    }

    private static bool SingleLink(int fd)
    {
        if (OperatingSystem.IsMacOS())
        {
            var attributes = new FileAttributesList {Count = 5, File = 1}; // ATTR_FILE_LINKCOUNT
            return FGetAttributes(fd, ref attributes, out var value, 8, 0) == 0 && value.Length == 8 && value.Links == 1;
        }
        const int atEmptyPath = 0x1000;
        const uint statxNlink = 4;
        return Statx(fd, "", atEmptyPath, statxNlink, out var result) == 0 && (result.Mask & statxNlink) != 0 && result.Links == 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributesList { internal ushort Count, Reserved; internal uint Common, Volume, Directory, File, Fork; }
    [StructLayout(LayoutKind.Sequential)]
    private struct LinkCountAttribute { internal uint Length, Links; }
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct StatxLinkCount { [FieldOffset(0)] internal uint Mask; [FieldOffset(16)] internal uint Links; }
    [DllImport("libc", EntryPoint = "fgetattrlist")]
    private static extern int FGetAttributes(int fd, ref FileAttributesList attributes, out LinkCountAttribute output, nuint length, uint options);
    [DllImport("libc", EntryPoint = "statx")]
    private static extern int Statx(int fd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, out StatxLinkCount result);
}
