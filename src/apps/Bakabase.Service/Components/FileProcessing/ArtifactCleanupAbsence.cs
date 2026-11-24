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
        var boundary = ArtifactManifestFiles.Absolute(root);
        return TryGetListingParent(boundary, Path.GetFullPath(path), ct, out _, out var missing) &&
            ArtifactManifestFiles.IsMissingFileConfirmed(boundary, missing, ct);
    }

    private static bool TryGetListingParent(string root, string path, CancellationToken ct, out string parent, out string missing)
    {
        // Several descendants may have been removed. Prove the first missing name in
        // its accessible parent, without ever climbing above the registered root.
        missing = path;
        parent = root;
        while (Path.GetDirectoryName(missing) is { } next)
        {
            ct.ThrowIfCancellationRequested();
            parent = next;
            ArtifactManifestFiles.RejectLinks(parent);
            try { return File.GetAttributes(parent).HasFlag(FileAttributes.Directory); }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
            {
                if (ArtifactManifestFiles.PathComparer.Equals(parent, root)) return false;
                missing = parent;
            }
        }
        return false;
    }

    internal static bool AllConfirmedAbsent(string root, IReadOnlyList<string> files, CancellationToken ct = default,
        Action<string>? observe = null) =>
        files.Count > 0 && ConfirmAbsentFiles(root, files, true, observe ?? ReadAttributes, ct) != null;

    // Only a negative proof: a listed name or a failed observation never authorizes unlinking.
    // In each independent pass, observe every exact name first, then enumerate its accessible
    // parent. A cached listing from before the last exact observation cannot mask a reappearance.
    internal static HashSet<string>? ConfirmAbsentFiles(string root, IReadOnlyList<string> paths, bool requireAll,
        Action<string> observe, CancellationToken ct, Func<string, IEnumerable<string>>? list = null)
    {
        var comparer = ArtifactManifestFiles.PathComparer;
        try
        {
            root = ArtifactManifestFiles.Absolute(root);
            var files = paths.Select(ArtifactManifestFiles.Absolute).ToHashSet(comparer);
            if (files.Count != paths.Count) return null;
            list ??= Directory.EnumerateFileSystemEntries;
            var missing = new HashSet<string>(comparer);
            for (var pass = 0; pass < 2; pass++)
            {
                ct.ThrowIfCancellationRequested();
                ArtifactManifestFiles.RejectLinks(root);
                if (!File.GetAttributes(root).HasFlag(FileAttributes.Directory)) return null;
                foreach (var path in pass == 0 ? files : missing)
                {
                    ct.ThrowIfCancellationRequested();
                    ArtifactManifestFiles.RelativeFile(root, path);
                    ArtifactManifestFiles.RejectLinks(path);
                    try { observe(path); }
                    catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
                    {
                        if (pass == 0) missing.Add(path);
                        continue;
                    }
                    if (requireAll || pass != 0) return null;
                }

                if (!NamesMissingFromListings(root, missing, list, ct)) return null;
            }
            return missing;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { return null; }
    }

    private static bool NamesMissingFromListings(string root, HashSet<string> missing,
        Func<string, IEnumerable<string>> list, CancellationToken ct)
    {
        var comparer = ArtifactManifestFiles.PathComparer;
        var children = new Dictionary<string, HashSet<string>>(comparer);
        foreach (var group in missing.GroupBy(path => Path.GetDirectoryName(path)!, comparer))
        {
            if (!TryGetListingParent(root, group.First(), ct, out var parent, out var name)) return false;
            if (!children.TryGetValue(parent, out var names)) children[parent] = names = new(comparer);
            if (comparer.Equals(parent, group.Key)) names.UnionWith(group);
            else names.Add(name);
        }

        // Always require a readable registered root, including when all the files
        // live deeper. Never substitute a listing above a detached mapping.
        if (!children.ContainsKey(root)) _ = list(root).Take(1).ToArray();
        foreach (var (directory, names) in children)
        {
            ArtifactManifestFiles.RejectLinks(directory);
            foreach (var entry in list(directory))
            {
                ct.ThrowIfCancellationRequested();
                if (names.Contains(entry)) return false;
            }
            ArtifactManifestFiles.RejectLinks(directory);
        }
        return true;
    }
}
