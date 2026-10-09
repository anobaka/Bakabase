using System;
using System.IO;

namespace Bakabase.Service.Components.ServerData;

/// <summary>Copy ordinary Unix access bits without exposing private data or inheriting special bits.</summary>
internal static class AppDataCopyPermissions
{
    private const UnixFileMode AccessBits = (UnixFileMode)0x1FF;
    private const UnixFileMode OwnerFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OwnerDirectory = OwnerFile | UnixFileMode.UserExecute;

    public static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else
        {
            Directory.CreateDirectory(path, OwnerDirectory);
            File.SetUnixFileMode(path, OwnerDirectory);
        }
    }

    public static void RestrictTargetRoot(string source, string target)
    {
        if (OperatingSystem.IsWindows()) return;
        // A source may rely on its root being private even when its files are 0644.
        // Never broaden an existing target's group/other access when adopting the source.
        var mode = File.GetUnixFileMode(source) & File.GetUnixFileMode(target) & AccessBits;
        File.SetUnixFileMode(target, mode | OwnerDirectory);
    }

    public static void CreateDirectory(string source, string target)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(target);
        else
        {
            var mode = (File.GetUnixFileMode(source) & AccessBits) | OwnerDirectory;
            Directory.CreateDirectory(target, mode);
            File.SetUnixFileMode(target, mode);
        }
    }

    public static FileStream CreateFile(string source, string target)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        var mode = OwnerFile;
        if (!OperatingSystem.IsWindows())
        {
            mode = (File.GetUnixFileMode(source) & AccessBits) | OwnerFile;
            // Apply restrictions at creation, before even the first byte can be observed.
            options.UnixCreateMode = mode;
        }
        var stream = new FileStream(target, options);
        try
        {
            // The process umask can remove source access bits at creation. Restore only
            // those requested above; owner write keeps SQLite/config work copies usable.
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, mode);
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }
}
