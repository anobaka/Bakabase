using System;
using System.IO;
using Bakabase.Abstractions.Components.FileSystem;
using Microsoft.Win32.SafeHandles;

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
        using var handle = new SafeFileHandle((IntPtr)fd, ownsHandle: false);
        return RegularFileHandle.HasSingleLink(handle);
    }
}
