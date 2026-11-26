using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Bakabase.Abstractions.Components.FileSystem;

public sealed record StorageVolumeCapacity(string VolumePath, long AvailableBytes, long TotalBytes);
internal readonly record struct StorageVolumeIdentity(string Capacity, string Mount);
public sealed record VerifiedStorageMount(string Identity, bool NativeFileIdentities);

/// <summary>Read-only actual server-volume measurement, shared by safety checks and capacity display.</summary>
public static class StorageVolumes
{
    internal const string DeploymentMountsVariable = "BAKABASE_DEPLOYMENT_MOUNTS";

    /// <summary>
    /// Identity for capacity accounting, distinct from the mount path shown to the user. Linux bind
    /// mounts (including container volumes) can expose one filesystem at several unrelated paths.
    /// Other platforms retain their resolved volume path; this does not identify shared allocation
    /// pools spanning separate filesystems, such as an APFS container or an overlay's backing store.
    /// </summary>
    public static string CapacityIdentity(StorageVolumeCapacity capacity) => Identify(capacity).Capacity;

    internal static StorageVolumeIdentity Identify(StorageVolumeCapacity capacity) => OperatingSystem.IsLinux()
        ? IdentifyLinuxMount(capacity.VolumePath, File.ReadLines("/proc/self/mountinfo"), capacity.TotalBytes,
            Environment.GetEnvironmentVariable(DeploymentMountsVariable))
        : new(capacity.VolumePath, capacity.VolumePath);

    internal static string LinuxCapacityIdentity(string volumePath, IEnumerable<string> mountInfo, long? totalBytes = null,
        string? deploymentMetadata = null) => IdentifyLinuxMount(volumePath, mountInfo, totalBytes, deploymentMetadata).Capacity;

    internal static StorageVolumeIdentity IdentifyLinuxMount(string volumePath, IEnumerable<string> mountInfo,
        long? totalBytes, string? deploymentMetadata)
    {
        var expected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(volumePath));
        var mounts = new List<LinuxMount>();
        foreach (var line in mountInfo)
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 10) continue;
            var separator = Array.IndexOf(fields, "-", 6);
            if (separator < 6 || separator + 3 >= fields.Length) continue;
            mounts.Add(new(fields[0], DecodeMountPath(fields[4]), DecodeMountPath(fields[3]), fields[2],
                fields[separator + 1] == "virtiofs" && fields[separator + 2] == "mac"));
        }
        var matching = mounts.Where(m => m.Target == expected).ToArray();
        if (matching.Length == 0) throw new IOException("The storage mount identity is unavailable.");
        var selected = matching[0];
        var device = selected.Device.Split(':');
        if (device.Length != 2 || !uint.TryParse(device[0], out var major) || !uint.TryParse(device[1], out var minor))
            throw new IOException("The storage mount identity is invalid.");
        if (matching.Length != 1)
            throw new IOException("The storage mount identity is ambiguous.");
        if (selected.Root.EndsWith(DeletedMountRootSuffix, StringComparison.Ordinal))
            throw new IOException("The storage mount root was removed.");
        var deviceIdentity = $"linux-device:{major}:{minor}";
        var identity = new StorageVolumeIdentity(deviceIdentity, $"{selected.Id}:{selected.Device}:{selected.Root}");
        if (!selected.IsMacVirtioFs || totalBytes is null) return identity;

        var claims = ReadHostCapacityClaims(deploymentMetadata);
        if (!claims.TryGetValue(selected.Target, out var selectedClaim) || selectedClaim.Root != selected.Root)
            return identity;
        if (totalBytes != selectedClaim.TotalBytes)
            throw new IOException("The storage mount capacity differs from its host deployment.");
        var peerTargets = new HashSet<string>(StringComparer.Ordinal);
        var hostDevices = new Dictionary<long, long>();
        foreach (var mount in mounts.Where(m => m.Device == selected.Device))
        {
            // Linux keeps the mount ID, device and former backing root of an unlinked bind.
            // Its capacity claim remains attributable, although it can no longer be a destination.
            if (!mount.IsMacVirtioFs || !claims.TryGetValue(mount.Target, out var claim) ||
                (claim.Root != mount.Root && claim.Root + DeletedMountRootSuffix != mount.Root) ||
                !peerTargets.Add(mount.Target))
                return identity;
            if (hostDevices.TryGetValue(claim.Device, out var known) && known != claim.TotalBytes)
                return identity;
            hostDevices[claim.Device] = claim.TotalBytes;
        }

        // Different APFS volumes may share one allocation pool. Leave groups with the
        // same measured total together, even when the host gives them different devices.
        return identity with { Capacity = $"{deviceIdentity}:mac-host-capacity:{totalBytes}" };
    }

    /// <summary>A single unambiguous Linux mount for these paths. Unknown network mounts and
    /// nested bind mounts cannot establish isolation for concurrent file operations.</summary>
    public static VerifiedStorageMount? VerifiedFileMount(string root, IReadOnlyList<string> files)
    {
        if (!OperatingSystem.IsLinux()) return null;
        var capacity = Measure(root);
        var lines = File.ReadAllLines("/proc/self/mountinfo");
        var identity = LinuxCapacityIdentity(capacity.VolumePath, lines, capacity.TotalBytes,
            Environment.GetEnvironmentVariable(DeploymentMountsVariable));
        string? type = null;
        var mount = Path.TrimEndingDirectorySeparator(capacity.VolumePath);
        foreach (var line in lines)
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var separator = Array.IndexOf(fields, "-", 6);
            if (separator < 6 || separator + 3 >= fields.Length) return null;
            var path = DecodeMountPath(fields[4]);
            if (path == mount)
            {
                if (type != null) return null;
                type = fields[separator + 1];
            }
            else if (Under(path, mount) && files.Any(file => file == path || Under(file, path))) return null;
        }
        if (type is "ext4" or "ext3" or "xfs" or "btrfs" or "f2fs") return new(identity, NativeFileIdentities: true);
        // Several host filesystems can share a guest st_dev. Only the host-attested mount
        // with its backing path, peer devices and measured capacity reconciled is usable.
        return type == "virtiofs" && identity.Contains(":mac-host-capacity:", StringComparison.Ordinal)
            ? new(identity, NativeFileIdentities: false) : null;

        static bool Under(string child, string parent) => child.StartsWith(
            Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string DecodeMountPath(string path) => Path.TrimEndingDirectorySeparator(
        path.Replace("\\040", " ").Replace("\\011", "\t").Replace("\\012", "\n").Replace("\\134", "\\"));

    private static Dictionary<string, MacHostCapacity> ReadHostCapacityClaims(string? metadata)
    {
        var result = new Dictionary<string, MacHostCapacity>(StringComparer.Ordinal);
        if (metadata is not { Length: > 0 and <= 65536 }) return result;
        try
        {
            using var document = JsonDocument.Parse(metadata);
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1) return result;
            var mounts = root.GetProperty("mounts");
            if (mounts.GetArrayLength() > 256) return result;
            var targets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mount in mounts.EnumerateArray())
            {
                var target = mount.GetProperty("target").GetString();
                if (target == null || !target.StartsWith('/')) return [];
                target = Path.TrimEndingDirectorySeparator(target);
                if (!targets.Add(target)) return [];
                if (mount.GetProperty("type").GetString() != "bind" ||
                    !mount.TryGetProperty("hostCapacity", out var capacity)) continue;
                var hostRoot = capacity.GetProperty("root").GetString();
                if (hostRoot == null || !hostRoot.StartsWith('/') ||
                    !capacity.GetProperty("device").TryGetInt64(out var device) || device < 0 ||
                    !capacity.GetProperty("totalBytes").TryGetInt64(out var total) || total <= 0)
                    return [];
                result.Add(target, new(Path.TrimEndingDirectorySeparator(hostRoot), device, total));
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return [];
        }
        return result;
    }

    private const string DeletedMountRootSuffix = "//deleted";
    private sealed record LinuxMount(string Id, string Target, string Root, string Device, bool IsMacVirtioFs);
    private sealed record MacHostCapacity(string Root, long Device, long TotalBytes);

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
