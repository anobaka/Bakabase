using System;
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
        Manifest.Entries.All(e => expected.Entries.Any(f => FileDispositionProofs.SameEntry(e, f))) &&
        (Manifest.Directories ?? []).ToHashSet(ArtifactManifestFiles.PathComparer)
            .SetEquals(expected.Directories ?? []);
}
