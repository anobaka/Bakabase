using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Bakabase.Abstractions.Components.FileSystem;

public sealed record StorageVolumeCapacity(string VolumePath, long AvailableBytes, long TotalBytes);

/// <summary>Read-only actual server-volume measurement, shared by safety checks and capacity display.</summary>
public static class StorageVolumes
{
    /// <summary>
    /// Identity for capacity accounting, distinct from the mount path shown to the user. Linux bind
    /// mounts (including container volumes) can expose one filesystem at several unrelated paths.
    /// Other platforms retain their resolved volume path; this does not identify shared allocation
    /// pools spanning separate filesystems, such as an APFS container or an overlay's backing store.
    /// </summary>
    public static string CapacityIdentity(StorageVolumeCapacity capacity) => OperatingSystem.IsLinux()
        ? LinuxCapacityIdentity(capacity.VolumePath, File.ReadLines("/proc/self/mountinfo"))
        : capacity.VolumePath;

    internal static string LinuxCapacityIdentity(string volumePath, IEnumerable<string> mountInfo)
    {
        var expected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(volumePath));
        string? identity = null;
        foreach (var line in mountInfo)
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 10 || Array.IndexOf(fields, "-", 6) < 6) continue;
            // mountinfo escapes whitespace and backslash in path fields using these octal values.
            var mount = fields[4].Replace("\\040", " ").Replace("\\011", "\t")
                .Replace("\\012", "\n").Replace("\\134", "\\");
            if (!string.Equals(Path.TrimEndingDirectorySeparator(mount), expected, StringComparison.Ordinal)) continue;
            var device = fields[2].Split(':');
            if (device.Length != 2 || !uint.TryParse(device[0], out var major) || !uint.TryParse(device[1], out var minor))
                throw new IOException("The storage mount identity is invalid.");
            var candidate = $"linux-device:{major}:{minor}";
            if (identity != null && identity != candidate)
                throw new IOException("The storage mount identity is ambiguous.");
            identity = candidate;
        }
        return identity ?? throw new IOException("The storage mount identity is unavailable.");
    }

    public static StorageVolumeCapacity Measure(string path)
    {
        var full = Path.GetFullPath(path);
        // Resolve existing symlink parents so a path alias cannot select the wrong storage volume.
        var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var segment in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            var info = new DirectoryInfo(current);
            if (info.LinkTarget != null)
                current = info.ResolveLinkTarget(true)?.FullName ?? throw new IOException("Storage link is unavailable.");
        }
        // Unmapped SMB shares do not appear in DriveInfo.GetDrives(). Query their share
        // directly, respecting the quota of the Windows identity performing the write.
        if (OperatingSystem.IsWindows() && MeasureWindowsUnc(current, ReadWindowsCapacity) is { } unc)
            return unc;
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var drive = DriveInfo.GetDrives().OrderByDescending(d => d.Name.Length).FirstOrDefault(d =>
        {
            var name = Path.TrimEndingDirectorySeparator(Path.GetFullPath(d.Name));
            return current.Equals(name, comparison) || current.StartsWith(Path.EndsInDirectorySeparator(name)
                ? name : name + Path.DirectorySeparatorChar, comparison);
        }) ?? throw new IOException("Storage volume is unavailable.");
        if (!drive.IsReady) throw new IOException("Storage volume is unavailable.");
        // macOS reserves /Volumes for mounts. A disconnected share must not silently
        // report the system disk underneath its former mount point.
        if (OperatingSystem.IsMacOS() && current.StartsWith("/Volumes/", StringComparison.Ordinal) && drive.Name == "/")
            throw new IOException("The requested storage mount is unavailable.");
        return new(drive.Name, drive.AvailableFreeSpace, drive.TotalSize);
    }

    internal static StorageVolumeCapacity? MeasureWindowsUnc(string path, Func<string, (ulong Available, ulong Total)> measure)
    {
        var normalized = path.Replace('/', '\\');
        var extended = normalized.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase);
        if (!extended && (!normalized.StartsWith(@"\\", StringComparison.Ordinal) ||
                         normalized.StartsWith(@"\\?\", StringComparison.Ordinal) || normalized.StartsWith(@"\\.\", StringComparison.Ordinal)))
            return null;
        var components = normalized[(extended ? 8 : 2)..].Split('\\');
        if (components.Length < 2 || components.Take(2).Any(c => string.IsNullOrWhiteSpace(c) || c is "." or ".."))
            throw new IOException("The network storage share is invalid.");
        var volume = @"\\" + components[0] + "\\" + components[1] + "\\";
        // GetDiskFreeSpaceExW requires a trailing separator for UNC names. Preserve the
        // extended prefix for long-path shares while keeping one canonical volume identity.
        var query = extended ? @"\\?\UNC\" + components[0] + "\\" + components[1] + "\\" : volume;
        var (available, total) = measure(query);
        return new(volume, Clamp(available), Clamp(total));
    }

    private static (ulong Available, ulong Total) ReadWindowsCapacity(string directory)
    {
        if (!GetDiskFreeSpaceExW(directory, out var available, out var total, out _))
            throw new IOException("The network storage capacity could not be read.", new Win32Exception(Marshal.GetLastWin32Error()));
        return (available, total);
    }

    private static long Clamp(ulong value) => value > long.MaxValue ? long.MaxValue : (long)value;

    // https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-getdiskfreespaceexw
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceExW(string directory, out ulong freeBytesAvailableToCaller,
        out ulong totalNumberOfBytes, out ulong totalNumberOfFreeBytes);
}
