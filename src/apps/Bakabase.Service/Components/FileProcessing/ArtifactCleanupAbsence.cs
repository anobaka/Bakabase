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
        Probe(root, path, ReadAttributes, ct);

    private static void ReadAttributes(string path) => _ = File.GetAttributes(path);

    // SMB can retain successful stat results for an unlinked name. Opening the exact
    // file observes the server namespace without reading the payload.
    internal static void OpenFileForObservation(string path)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    }

    internal static ArtifactCleanupPathPresence Probe(string root, string path,
        Action<string> observe, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            ArtifactManifestFiles.RelativeFile(ArtifactManifestFiles.Absolute(root), ArtifactManifestFiles.Absolute(path));
            ArtifactManifestFiles.RejectLinks(path);
            // A readable higher filesystem ancestor is not a substitute for this registered root.
            _ = Directory.EnumerateFileSystemEntries(root).Take(1).ToArray();
            try { observe(path); return ArtifactCleanupPathPresence.Present; }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { }
            if (!MissingWithinReadableRoot(root, path, ct)) return ArtifactCleanupPathPresence.Unavailable;
            // A path that reappeared, a changed link or a detached parent cannot complete a no-op.
            ArtifactManifestFiles.RejectLinks(path);
            try { observe(path); return ArtifactCleanupPathPresence.Present; }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { }
            return MissingWithinReadableRoot(root, path, ct)
                ? ArtifactCleanupPathPresence.ConfirmedAbsent : ArtifactCleanupPathPresence.Unavailable;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { return ArtifactCleanupPathPresence.Unavailable; }
    }

    // One snapshot per read-only selection or post-unlink receipt. A directory with thousands
    // of siblings is enumerated once, and the next preview or mutation starts a new snapshot.
    internal sealed class DirectoryListing
    {
        private readonly Dictionary<string, HashSet<string>> directories = new(ArtifactManifestFiles.PathComparer);

        internal bool Contains(string directory, string path, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!directories.TryGetValue(directory, out var entries))
            {
                ArtifactManifestFiles.RejectLinks(directory);
                entries = new(ArtifactManifestFiles.PathComparer);
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    ct.ThrowIfCancellationRequested();
                    entries.Add(entry);
                }
                directories.Add(directory, entries);
            }
            return entries.Contains(path);
        }
    }

    // The processing ledger identifies the candidates. A listing can corroborate those names,
    // but it never adds neighbours (including SMB delete markers) to the selection.
    internal static IReadOnlyList<string> ConfirmReportedFiles(string root, IEnumerable<string> reportedFiles,
        CancellationToken ct)
    {
        var present = new List<string>();
        var listing = new DirectoryListing();
        foreach (var path in reportedFiles)
        {
            ArtifactManifestFiles.RelativeFile(root, path);
            var directory = Path.GetDirectoryName(path)!;
            try
            {
                if (listing.Contains(directory, path, ct)) { present.Add(path); continue; }
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
            { /* Resolve a vanished parent within the recorded checkpoint below. */ }
            // Normal previews need no payload handle. Only conflicting metadata requires
            // an open and independent directory checks under the checkpoint root.
            switch (Probe(root, path, OpenFileForObservation, ct))
            {
                case ArtifactCleanupPathPresence.Present: present.Add(path); break;
                case ArtifactCleanupPathPresence.Unavailable:
                    throw new IOException("The processing file or its storage could not be verified.");
            }
        }
        return present;
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

    internal static bool AllConfirmedAbsent(string root, IReadOnlyList<string> files, CancellationToken ct = default,
        Action<string>? observe = null) =>
        files.Count > 0 && files.Distinct(ArtifactManifestFiles.PathComparer).Count() == files.Count &&
        files.All(path => Probe(root, path, observe ?? ReadAttributes, ct) == ArtifactCleanupPathPresence.ConfirmedAbsent);
}
