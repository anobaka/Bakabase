using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Bakabase.InsideWorld.Business.Components.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>Request-local, explicitly accepted metadata verification. This never authorizes
/// a file or overrides ownership, lineage, references or a storage boundary. Callers must
/// first select files from immutable executor/manifest evidence under their existing fences.
/// No file contents are read; choosing this mode explicitly forgoes SHA verification.</summary>
internal sealed class ArtifactCleanupFileSnapshot
{
    private sealed record Identity(long Length, DateTime Created, DateTime Written);
    private readonly Dictionary<string, Identity> files = new(FileProcessingFiles.PathComparer);

    internal static ArtifactCleanupFileSnapshot? For(ArtifactDeliveryCleanupAuthorization? authorization) =>
        authorization?.Consent is {DeliveryAccepted: true, SkipFileVerification: true} ? new() : null;

    internal void Observe(string path, long expectedLength, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        path = Path.GetFullPath(path);
        var current = Read(path);
        if (current.Length != expectedLength || files.TryGetValue(path, out var previous) && current != previous)
            throw new IOException("A selected cleanup file changed; it was retained.");
        files.TryAdd(path, current);
    }

    internal bool Matches(string path, long expectedLength, CancellationToken ct)
    {
        path = Path.GetFullPath(path);
        if (!files.ContainsKey(path)) return false;
        Observe(path, expectedLength, ct);
        return true;
    }

    internal void Moved(string source, string destination, long expectedLength, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        source = Path.GetFullPath(source); destination = Path.GetFullPath(destination);
        var current = Read(destination);
        if (!files.TryGetValue(source, out var previous) || current != previous || current.Length != expectedLength)
            throw new IOException("A selected cleanup file changed during relocation; it was retained.");
        files[destination] = current;
    }

    private static Identity Read(string path)
    {
        ArtifactManifestFiles.RejectLinks(path);
        if (File.GetAttributes(path).HasFlag(FileAttributes.Directory))
            throw new IOException("A selected cleanup file was replaced by a directory.");
        // Open a read handle to surface unavailable mounts/access errors. Metadata-only
        // File.Exists would hide those errors and can return cached SMB observations.
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var identity = new Identity(RandomAccess.GetLength(handle), File.GetCreationTimeUtc(handle), File.GetLastWriteTimeUtc(handle));
        ArtifactManifestFiles.RejectLinks(path);
        var information = new FileInfo(path); information.Refresh();
        if (!information.Exists || information.Attributes.HasFlag(FileAttributes.Directory) ||
            information.Length != identity.Length || information.CreationTimeUtc != identity.Created || information.LastWriteTimeUtc != identity.Written)
            throw new IOException("A selected cleanup file changed while its identity was checked.");
        return identity;
    }
}
