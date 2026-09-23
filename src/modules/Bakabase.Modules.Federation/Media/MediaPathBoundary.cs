namespace Bakabase.Modules.Federation.Media;

/// <summary>Resolves every ancestor link before comparing boundaries, including mounted-path mappings.</summary>
public static class MediaPathBoundary
{
    public static string ResolvePhysical(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var segment in full[root.Length..].Split(Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget != null)
                current = info.ResolveLinkTarget(true)?.FullName ?? throw new IOException("Unresolved filesystem link.");
        }
        return Path.GetFullPath(current);
    }

    public static bool IsWithin(string root, string path)
    {
        try
        {
            var physicalRoot = ResolvePhysical(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var physicalPath = ResolvePhysical(path);
            return physicalPath.StartsWith(physicalRoot,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    public static string? Map(string root, string relativePath)
        => MapLocation(root, relativePath, false);

    public static string? MapLocation(string root, string relativePath, bool isDirectory)
    {
        if (isDirectory && relativePath == "." && !string.IsNullOrWhiteSpace(root))
        {
            try { return Directory.Exists(root) ? ResolvePhysical(root) : null; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
        }
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathRooted(relativePath) || relativePath.Contains('\\') || relativePath.Contains(':') ||
            relativePath.Split('/').Any(s => s is "" or "." or "..")) return null;
        try
        {
            var full = Path.GetFullPath(Path.Combine(root, relativePath));
            return IsWithin(root, full) && (isDirectory ? Directory.Exists(full) : File.Exists(full)) ? full : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
