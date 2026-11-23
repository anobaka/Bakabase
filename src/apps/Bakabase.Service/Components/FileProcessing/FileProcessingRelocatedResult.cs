using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>A completed stage-commit receipt can relocate the immutable result used to
/// verify private snapshot cleanup. It never changes the private selection or its lineage.</summary>
internal sealed record FileProcessingRelocatedResult(string OriginalRoot, string VerificationRoot, ArtifactManifest Manifest)
{
    internal bool Matches(string originalRoot, ArtifactManifest expected) =>
        ArtifactManifestFiles.PathComparer.Equals(Path.GetFullPath(OriginalRoot), Path.GetFullPath(originalRoot)) &&
        Manifest.Fingerprint == expected.Fingerprint && Manifest.Entries.Count == expected.Entries.Count &&
        CoversEntries(expected.Entries, Manifest.Entries) &&
        (Manifest.Directories ?? []).ToHashSet(ArtifactManifestFiles.PathComparer)
            .SetEquals(expected.Directories ?? []);

    internal static bool CoversEntries(IReadOnlyList<ArtifactManifestEntry> manifest, IReadOnlyList<ArtifactManifestEntry> required)
    {
        if (required.Count > manifest.Count || IndexEntries(manifest) is not {} remaining) return false;
        foreach (var entry in required)
        {
            if (entry?.RelativePath == null || !remaining.Remove(entry.RelativePath, out var match) ||
                !FileDispositionProofs.SameEntry(entry, match)) return false;
        }
        return true;
    }

    internal static Dictionary<string, ArtifactManifestEntry>? IndexEntries(IReadOnlyList<ArtifactManifestEntry> entries)
    {
        var index = new Dictionary<string, ArtifactManifestEntry>(entries.Count, StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry?.RelativePath == null || !index.TryAdd(entry.RelativePath, entry)) return null;
        }
        return index;
    }
}
