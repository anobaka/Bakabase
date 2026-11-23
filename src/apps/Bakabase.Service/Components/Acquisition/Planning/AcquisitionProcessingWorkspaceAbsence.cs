using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Confirm missing receipt slots in the private, flat processing quarantine.
/// File names at their original stage location may already belong to the user.</summary>
internal sealed class AcquisitionProcessingWorkspaceAbsence(string root, string directory, CancellationToken ct,
    Func<string, IEnumerable<string>>? listEntries = null)
{
    internal const int BatchSize = 512;
    private readonly HashSet<string> pending = new(ArtifactManifestFiles.PathComparer);

    internal void Add(int index)
    {
        ct.ThrowIfCancellationRequested();
        var name = index.ToString("X8", CultureInfo.InvariantCulture);
        RequireMissing(name);
        pending.Add(name);
        if (pending.Count == BatchSize) Flush();
    }

    internal void Flush()
    {
        if (pending.Count == 0) return;
        // Share the two parent enumerations across this batch. Exact attribute checks also
        // catch aliases on case-insensitive volumes mounted under a case-sensitive host.
        CheckDirectory(pending.Contains);
        foreach (var name in pending) RequireMissing(name);
        CheckDirectory(pending.Contains);
        pending.Clear();
    }

    internal void VerifyBeforeRetirement(int fileCount)
    {
        bool IsReceiptSlot(string name)
        {
            if (name.Length != 8 || !int.TryParse(name, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture,
                    out var slot) || slot < 0 || slot >= fileCount) return false;
            var expected = slot.ToString("X8", CultureInfo.InvariantCulture);
            if (ArtifactManifestFiles.PathComparer.Equals(expected, name)) return true;
            RequireMissing(expected);
            return false;
        }
        CheckDirectory(IsReceiptSlot);
        CheckDirectory(IsReceiptSlot);
    }

    private void RequireMissing(string name)
    {
        ct.ThrowIfCancellationRequested();
        try { _ = File.GetAttributes(Path.Combine(directory, name)); }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        throw new IOException("files_changed");
    }

    private void CheckDirectory(Func<string, bool> selected)
    {
        ct.ThrowIfCancellationRequested();
        ArtifactManifestFiles.RelativeFile(root, directory);
        ArtifactManifestFiles.RejectLinks(directory);
        // A missing mount or unreadable allocation cannot establish absence.
        using (var entries = Directory.EnumerateFileSystemEntries(root).GetEnumerator()) entries.MoveNext();
        FileAttributes attributes;
        try { attributes = File.GetAttributes(directory); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            // A prior pass can remove the empty quarantine before saving its final receipt.
            if (ArtifactCleanupAbsence.Probe(root, directory, ct) == ArtifactCleanupPathPresence.ConfirmedAbsent) return;
            throw new IOException("files_changed", e);
        }
        if (!attributes.HasFlag(FileAttributes.Directory) || attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("files_changed");
        foreach (var entry in (listEntries ?? Directory.EnumerateFileSystemEntries)(directory))
        {
            ct.ThrowIfCancellationRequested();
            if (selected(Path.GetFileName(entry))) throw new IOException("files_changed");
        }
        ArtifactManifestFiles.RejectLinks(directory);
        if (!File.GetAttributes(directory).HasFlag(FileAttributes.Directory)) throw new IOException("files_changed");
        using var rootCheck = Directory.EnumerateFileSystemEntries(root).GetEnumerator();
        rootCheck.MoveNext();
    }
}
