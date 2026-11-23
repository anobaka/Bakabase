using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>Identifies a confirmed directory across inode changes on virtual mounts.</summary>
internal static class DirectoryCleanupIdentity
{
    private const string Attribute = "user.bakabase.cleanupIdentity";
    private const int Size = 16;

    internal static Guid? GetOrCreate(int fd)
    {
        var token = Guid.NewGuid();
        var bytes = token.ToByteArray();
        // XATTR_CREATE prevents simultaneous receipts from replacing one another's identity.
        var result = OperatingSystem.IsMacOS()
            ? SetDarwin(fd, Attribute, bytes, Size, 0, 2)
            : SetLinux(fd, Attribute, bytes, Size, 1);
        if (result == 0) return token;
        var error = Marshal.GetLastPInvokeError();
        // Filesystems that do not support writable attributes keep exact device/inode checking.
        return error == 17 ? Read(fd) : null; // EEXIST
    }

    internal static Guid? Read(int fd)
    {
        var bytes = new byte[Size];
        var result = OperatingSystem.IsMacOS()
            ? GetDarwin(fd, Attribute, bytes, Size, 0, 0)
            : GetLinux(fd, Attribute, bytes, Size);
        if (result >= 0) return result == Size ? new Guid(bytes) : null;
        var error = Marshal.GetLastPInvokeError();
        if (error == 34 || error == (OperatingSystem.IsMacOS() ? 93 : 61)) return null; // ERANGE, ENOATTR / ENODATA
        // A temporarily unavailable volume cannot establish that the directory was replaced.
        throw new IOException($"Cannot read the directory cleanup identity (errno {error}).");
    }

    [DllImport("libc", EntryPoint = "fsetxattr", SetLastError = true)]
    private static extern int SetLinux(int fd, string name, byte[] value, nuint size, int flags);
    [DllImport("libc", EntryPoint = "fgetxattr", SetLastError = true)]
    private static extern nint GetLinux(int fd, string name, [Out] byte[] value, nuint size);
    [DllImport("libc", EntryPoint = "fsetxattr", SetLastError = true)]
    private static extern int SetDarwin(int fd, string name, byte[] value, nuint size, uint position, int options);
    [DllImport("libc", EntryPoint = "fgetxattr", SetLastError = true)]
    private static extern nint GetDarwin(int fd, string name, [Out] byte[] value, nuint size, uint position, int options);
}
