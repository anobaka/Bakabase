using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Bakabase.Service.Components.CloudDownloads.Mega;

/// <summary>MEGAcmd has no POSIX config-directory flag. Adopt only a profile created by this
/// application, never a user's existing session. HOME is neither changed nor redirected.</summary>
internal static class MegaCmdProfile
{
    private sealed record Ownership(int Version, string NativePath, string ManagedPath);
    internal static void Ensure(string nativePath, string managedPath, bool create)
    {
        nativePath = Path.GetFullPath(nativePath); managedPath = Path.GetFullPath(managedPath);
        var marker = Path.Combine(managedPath, ".bakabase-megacmd-profile");
        var managed = new DirectoryInfo(managedPath);
        if (managed.LinkTarget != null || File.Exists(managedPath)) throw new InvalidOperationException("MEGA_PROFILE_CONFLICT");
        var owned = false;
        if (File.Exists(marker))
        {
            try
            {
                if (new FileInfo(marker).LinkTarget != null || new FileInfo(marker).Length > 8192)
                    throw new InvalidOperationException("MEGA_PROFILE_CONFLICT");
                var ownership = JsonSerializer.Deserialize<Ownership>(File.ReadAllText(marker));
                owned = ownership == new Ownership(1, nativePath, managedPath);
            }
            catch (JsonException) { throw new InvalidOperationException("MEGA_PROFILE_CONFLICT"); }
            if (!owned) throw new InvalidOperationException("MEGA_PROFILE_CONFLICT");
        }
        // Do not turn an existing session in the intended managed path into an owned profile
        // merely because the native path happens to be absent.
        if (managed.Exists && !owned && Directory.EnumerateFileSystemEntries(managedPath).Any())
            throw new InvalidOperationException("MEGA_PROFILE_CONFLICT");
        var native = new DirectoryInfo(nativePath);
        if (native.LinkTarget != null)
        {
            var target = native.ResolveLinkTarget(true)?.FullName;
            if (target != managedPath || !owned)
                throw new InvalidOperationException("MEGA_PROFILE_CONFLICT");
            return;
        }
        if (native.Exists || File.Exists(nativePath)) throw new InvalidOperationException("MEGA_PROFILE_CONFLICT");
        if (!create) return;
        Directory.CreateDirectory(managedPath);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(managedPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (!owned)
        {
            using var file = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(file, new Ownership(1, nativePath, managedPath));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(marker, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        // Do not register the upstream updater for this managed optional dependency.
        if (!File.Exists(Path.Combine(managedPath, "megacmd.cfg")))
            File.WriteAllText(Path.Combine(managedPath, "megacmd.cfg"), "updaterregistered=true\n");
        Directory.CreateSymbolicLink(nativePath, managedPath);
    }
}
