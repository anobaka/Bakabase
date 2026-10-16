using System;
using System.IO;
using System.Linq;

namespace Bakabase.Service.Components.Federation.Processing;

internal static class ResourceProcessingPaths
{
    internal static string Relative(string root, bool isFile, string file)
    {
        var full = Path.GetFullPath(file);
        var relative = Path.GetRelativePath(Path.GetFullPath(root), full).Replace('\\', '/');
        if (isFile && relative == ".") return "";
        ValidateRelative(relative);
        RejectLinks(root, full, isFile);
        return relative;
    }
    internal static void ValidateRelative(string value)
    {
        if (value == "") return; // the resource itself when it is a file
        if (Path.IsPathRooted(value) || value.Contains('\\') || value.Contains(':') ||
            value.Split('/').Any(p => p is "" or "." or ".."))
            throw new ResourceProcessingException("UnsafePath", "A processing result must stay within its resource.");
    }
    internal static string OwnerFile(string ownerRoot, bool isFile, string relative)
    {
        ValidateRelative(relative);
        if (isFile)
        {
            if (relative != "") throw new ResourceProcessingException("UnsafePath", "A file resource cannot contain child files.");
            return ownerRoot;
        }
        if (relative.Length == 0) throw new ResourceProcessingException("UnsafePath", "Choose a child file of the resource.");
        var separator = ownerRoot.Contains('\\') ? '\\' : '/';
        return ownerRoot.TrimEnd('/', '\\') + separator + relative.Replace('/', separator);
    }
    internal static void RejectLinks(string root, string path, bool isFile)
    {
        var fullRoot = Path.GetFullPath(root);
        var cursor = Path.GetFullPath(path);
        while (true)
        {
            if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new ResourceProcessingException("UnsafePath", "Processing does not follow symbolic links inside a resource.");
            if (string.Equals(cursor, fullRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) break;
            if (isFile || (cursor = Path.GetDirectoryName(cursor)!) == null)
                throw new ResourceProcessingException("UnsafePath", "The file is outside its resource.");
        }
    }
}
