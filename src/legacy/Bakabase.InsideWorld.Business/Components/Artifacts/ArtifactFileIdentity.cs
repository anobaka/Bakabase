using System;
using System.IO;
using Bakabase.Abstractions.Components.FileSystem;

namespace Bakabase.InsideWorld.Business.Components.Artifacts;

public static class ArtifactFileIdentity
{
    public static void RequireRegularFile(string path) => RegularFileHandle.RequireRegularFile(path);

    /// <summary>A filesystem object identity that remains stable when a regular file moves on the same volume.</summary>
    public static string Read(string path, bool allowConcurrentWrites = false)
    {
        ArtifactManifestFiles.RejectLinks(path);
        var share = allowConcurrentWrites ? FileShare.ReadWrite | FileShare.Delete : FileShare.Read;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, share);
        return RegularFileHandle.Identity(stream, path);
    }

    public static string? ReadContentGeneration(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return null;
        ArtifactManifestFiles.RejectLinks(path);
        using var stream = File.OpenRead(path);
        return ReadContentGeneration(stream, path);
    }

    internal static string? ReadContentGeneration(FileStream stream, string path) => RegularFileHandle.ReadContentGeneration(stream, path);
    internal static bool? MatchesPath(FileStream stream, string path) => RegularFileHandle.MatchesPath(stream, path);
    internal static bool SameRegularFile(FileStream first, FileStream second, string path) => RegularFileHandle.SameRegularFile(first, second, path);
}
