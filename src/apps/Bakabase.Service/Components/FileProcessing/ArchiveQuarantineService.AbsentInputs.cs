using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

public sealed partial class ArchiveQuarantineService
{
    internal async Task<(int Files, long Bytes)?> PendingDeletionSizeAsync(ArchiveQuarantineReceipt header,
        ArtifactManifest manifest, CancellationToken ct)
    {
        try
        {
            var receipt = await Read(header.ReceiptId, ct);
            if (receipt.Id != header.ReceiptId || receipt.ArtifactId != header.ArtifactId ||
                receipt.Status != header.Status || !receipt.OutputIds.SequenceEqual(header.OutputArtifactIds) ||
                !ArtifactManifestFiles.PathComparer.Equals(Decode(receipt.OriginalRoot), header.OriginalRoot) ||
                !ArtifactManifestFiles.PathComparer.Equals(Decode(receipt.QuarantineRoot), header.QuarantineRoot) ||
                !FileDispositionProofs.SameManifest(receipt.Manifest, manifest)) return null;
            var excluded = MissingDeletionEntries(receipt);
            var files = 0;
            long bytes = 0;
            foreach (var entry in manifest.Entries)
            {
                if (excluded.Contains(entry.RelativePath)) continue;
                ct.ThrowIfCancellationRequested();
                var root = header.QuarantineRoot;
                var held = FileProcessingFiles.Within(Path.Combine(root, entry.RelativePath), root);
                if (header.Status == "Moving" && !File.Exists(held))
                {
                    root = header.OriginalRoot;
                    held = FileProcessingFiles.Within(Path.Combine(root, entry.RelativePath), root);
                }
                var presence = ArtifactCleanupAbsence.Probe(root, held, ct);
                if (presence == ArtifactCleanupPathPresence.Unavailable) return null;
                if (presence == ArtifactCleanupPathPresence.ConfirmedAbsent) continue;
                if (new FileInfo(held).Length != entry.Length) return null;
                files++;
                bytes += entry.Length;
            }
            return (files, bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or
                                     ArgumentException or System.Text.Json.JsonException) { return null; }
    }

    internal static (int Files, long Bytes)? PreviewMovedSourceSize(ArtifactRecord source,
        IReadOnlySet<string> moved, CancellationToken ct)
    {
        try
        {
            ArtifactManifestFiles.RejectLinks(source.RootDirectory);
            ct.ThrowIfCancellationRequested();
            // An empty, readable source is common after a whole stage handoff. Inspect
            // it once even when tens of thousands of moved entries share the same root.
            if (!Directory.EnumerateFileSystemEntries(source.RootDirectory).Any())
                return moved.Count == source.Manifest.Entries.Count ? (0, 0L) : null;
            var branches = new Dictionary<string, ArtifactCleanupPathPresence>(StringComparer.Ordinal);
            var files = 0;
            long bytes = 0;
            foreach (var entry in source.Manifest.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (moved.Contains(entry.RelativePath))
                {
                    var separator = entry.RelativePath.IndexOf('/');
                    if (separator >= 0)
                    {
                        var top = entry.RelativePath[..separator];
                        if (!branches.TryGetValue(top, out var presence))
                            branches[top] = presence = ArtifactCleanupAbsence.Probe(source.RootDirectory, Path.Combine(source.RootDirectory, top), ct);
                        if (presence == ArtifactCleanupPathPresence.ConfirmedAbsent) continue;
                        if (presence == ArtifactCleanupPathPresence.Unavailable) return null;
                    }
                    if (ArtifactCleanupAbsence.Probe(source.RootDirectory, Path.Combine(source.RootDirectory, entry.RelativePath), ct) !=
                        ArtifactCleanupPathPresence.ConfirmedAbsent)
                        return null;
                }
                else
                {
                    var path = Path.Combine(source.RootDirectory, entry.RelativePath);
                    if (ArtifactCleanupAbsence.Probe(source.RootDirectory, path, ct) !=
                        ArtifactCleanupPathPresence.Present || new FileInfo(path).Length != entry.Length) return null;
                    files++;
                    bytes += entry.Length;
                }
            }
            return (files, bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        { return null; }
    }

    private static HashSet<string> ConfirmAbsentEntries(string root, ArtifactManifest manifest, CancellationToken ct)
    {
        var paths = ArtifactManifestFiles.ResolveFiles(root, manifest);
        ArtifactManifestFiles.RejectLinks(root);
        if (!Directory.Exists(root)) throw new IOException("The original cleanup directory is unavailable.");
        var absent = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < paths.Count; index++)
        {
            var presence = ArtifactCleanupAbsence.Probe(root, paths[index], ct);
            if (presence == ArtifactCleanupPathPresence.Unavailable)
                throw new IOException("The cleanup directory could not be read.");
            if (presence == ArtifactCleanupPathPresence.ConfirmedAbsent) absent.Add(manifest.Entries[index].RelativePath);
        }
        return absent;
    }

    private static IReadOnlySet<string> MissingDeletionEntries(Receipt receipt)
    {
        var absent = receipt.AbsentBeforeDeletion.ToHashSet(StringComparer.Ordinal);
        if (absent.Count == 0) return absent;
        var registered = receipt.Manifest.Entries.Select(e => e.RelativePath).ToHashSet(StringComparer.Ordinal);
        var moved = receipt.MovedBeforeDeletion.ToHashSet(StringComparer.Ordinal);
        if (absent.Count != receipt.AbsentBeforeDeletion.Count || !absent.IsSubsetOf(registered) ||
            moved.Count != receipt.MovedBeforeDeletion.Count || !moved.IsSubsetOf(absent) ||
            !receipt.Consents.Any(c => c.DeliveryAccepted && c.SkipFileVerification) && !absent.SetEquals(moved))
            throw new InvalidDataException("Invalid already-removed cleanup entry proof.");
        return absent;
    }

    private async Task VerifyMovedEntriesAsync(Receipt receipt, ArtifactRecord source,
        Func<ArtifactRecord, CancellationToken, Task<IReadOnlySet<string>>>? resolve, CancellationToken ct)
    {
        if (receipt.MovedBeforeDeletion.Count == 0 && receipt.MovedDirectoriesBeforeDeletion.Count == 0) return;
        var originalRoot = Decode(receipt.OriginalRoot);
        var original = source with {RootDirectory = originalRoot,
            Files = ArtifactManifestFiles.ResolveFiles(originalRoot, source.Manifest)};
        var proven = resolve == null ? null : await resolve(original, ct);
        var directories = receipt.MovedDirectoriesBeforeDeletion.ToHashSet(StringComparer.Ordinal);
        if (directories.Count != receipt.MovedDirectoriesBeforeDeletion.Count || !directories.IsSubsetOf(source.Manifest.Directories ?? []) ||
            proven == null || receipt.MovedBeforeDeletion.Concat(directories).Any(path => !proven.Contains(path)))
            throw new InvalidDataException("The stage relocation proof changed; the original files were retained.");
        foreach (var directory in directories) RequireAbsent(originalRoot, Path.Combine(originalRoot, directory), ct);
    }

    private static void RequireAbsent(string root, string path, CancellationToken ct)
    {
        if (ArtifactCleanupAbsence.Probe(root, path, ct) != ArtifactCleanupPathPresence.ConfirmedAbsent)
            throw new IOException("An already-removed cleanup entry reappeared or its storage became unavailable.");
    }

    private void VerifyRecordedAbsence(Receipt receipt, CancellationToken ct)
    {
        var absent = MissingDeletionEntries(receipt);
        if (absent.Count == 0) return;
        var relocate = ReadReceiptLocationSnapshot(ct);
        var original = DecodeLocation(receipt.OriginalRoot, _appData, relocate);
        var quarantine = DecodeLocation(receipt.QuarantineRoot, _appData, relocate);
        foreach (var relative in absent)
        {
            RequireAbsent(original, Path.Combine(original, relative), ct);
            RequireAbsent(quarantine, Path.Combine(quarantine, relative), ct);
        }
        var current = ReadReceiptLocationSnapshot(ct);
        if (!ArtifactManifestFiles.PathComparer.Equals(original, DecodeLocation(receipt.OriginalRoot, _appData, current)) ||
            !ArtifactManifestFiles.PathComparer.Equals(quarantine, DecodeLocation(receipt.QuarantineRoot, _appData, current)))
            throw new IOException("The registered cleanup roots changed during verification.");
    }
}
