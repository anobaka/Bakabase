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
        if (receipt.MovedBeforeDeletion.Count == 0) return;
        var originalRoot = Decode(receipt.OriginalRoot);
        var original = source with {RootDirectory = originalRoot,
            Files = ArtifactManifestFiles.ResolveFiles(originalRoot, source.Manifest)};
        var proven = resolve == null ? null : await resolve(original, ct);
        if (proven == null || receipt.MovedBeforeDeletion.Any(path => !proven.Contains(path)))
            throw new InvalidDataException("The stage relocation proof changed; the original files were retained.");
    }

    private static void RequireAbsent(string root, string path, CancellationToken ct)
    {
        if (ArtifactCleanupAbsence.Probe(root, path, ct) != ArtifactCleanupPathPresence.ConfirmedAbsent)
            throw new IOException("An already-removed cleanup entry reappeared or its storage became unavailable.");
    }

    private void VerifyRecordedAbsence(Receipt receipt, CancellationToken ct)
    {
        foreach (var relative in MissingDeletionEntries(receipt))
        {
            RequireAbsent(Decode(receipt.OriginalRoot), Path.Combine(Decode(receipt.OriginalRoot), relative), ct);
            RequireAbsent(Decode(receipt.QuarantineRoot), Path.Combine(Decode(receipt.QuarantineRoot), relative), ct);
        }
    }
}
