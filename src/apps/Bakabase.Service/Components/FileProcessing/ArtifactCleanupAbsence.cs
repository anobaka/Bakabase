using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Bakabase.InsideWorld.Business.Components.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

internal enum ArtifactCleanupPathPresence { Present, ConfirmedAbsent, Unavailable }

/// <summary>Absence is an observed no-op, never permission to delete or retire an artifact.</summary>
internal static class ArtifactCleanupAbsence
{
    internal static ArtifactCleanupPathPresence Probe(string root, string path, CancellationToken ct = default) =>
        Probe(root, path, File.GetAttributes, ct);

    internal static ArtifactCleanupPathPresence Probe(string root, string path,
        Func<string, FileAttributes> attributes, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            ArtifactManifestFiles.RelativeFile(ArtifactManifestFiles.Absolute(root), ArtifactManifestFiles.Absolute(path));
            ArtifactManifestFiles.RejectLinks(path);
            // A readable higher filesystem ancestor is not a substitute for this registered root.
            _ = Directory.EnumerateFileSystemEntries(root).Take(1).ToArray();
            try { _ = attributes(path); return ArtifactCleanupPathPresence.Present; }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { }
            if (!MissingWithinReadableRoot(root, path, ct)) return ArtifactCleanupPathPresence.Unavailable;
            // A path that reappeared, a changed link or a detached parent cannot complete a no-op.
            ArtifactManifestFiles.RejectLinks(path);
            try { _ = attributes(path); return ArtifactCleanupPathPresence.Present; }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { }
            return MissingWithinReadableRoot(root, path, ct)
                ? ArtifactCleanupPathPresence.ConfirmedAbsent : ArtifactCleanupPathPresence.Unavailable;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { return ArtifactCleanupPathPresence.Unavailable; }
    }

    private static bool MissingWithinReadableRoot(string root, string path, CancellationToken ct)
    {
        var boundary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var missing = Path.GetFullPath(path);
        // A successful cleanup may remove several empty descendants. Prove the first
        // missing entry below an accessible parent, without ever climbing above the
        // registered root. A detached root, link, file-as-parent or IO error is not absence.
        while (Path.GetDirectoryName(missing) is { } parent)
        {
            ct.ThrowIfCancellationRequested();
            ArtifactManifestFiles.RejectLinks(parent);
            try
            {
                var attributes = File.GetAttributes(parent);
                if (!attributes.HasFlag(FileAttributes.Directory)) return false;
                return ArtifactManifestFiles.IsMissingFileConfirmed(boundary, missing, ct);
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
            {
                if (ArtifactManifestFiles.PathComparer.Equals(parent, boundary)) return false;
                missing = parent;
            }
        }
        return false;
    }

    internal static bool AllConfirmedAbsent(string root, IReadOnlyList<string> files, CancellationToken ct = default) =>
        files.Count > 0 && files.Distinct(ArtifactManifestFiles.PathComparer).Count() == files.Count &&
        files.All(path => Probe(root, path, ct) == ArtifactCleanupPathPresence.ConfirmedAbsent);
}
