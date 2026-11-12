using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Service.Components.CloudDownloads;

/// <summary>Optional, offline proof of provider-owned layout in a retained completed download.</summary>
public interface ICloudDownloadDirectoryProvenanceProvider
{
    Task<CloudDownloadDirectoryEvidence?> InspectDirectoryEvidenceAsync(CloudDownloadRequest request,
        string artifactRoot, CancellationToken ct);
}

/// <summary>Exact provider receipt membership. A name or a partial subset is never sufficient.</summary>
public sealed record CloudDownloadDirectoryEvidence(string RelativeDirectory, IReadOnlyDictionary<string, long> Files)
{
    public ArtifactDirectoryProvenance? Bind(ArtifactManifest manifest)
    {
        if (Files.Count == 0 || manifest.Entries.Count != Files.Count ||
            manifest.Entries.Select(e => e.RelativePath).Distinct(StringComparer.Ordinal).Count() != Files.Count ||
            manifest.Entries.Any(e => !Files.TryGetValue(e.RelativePath, out var size) || size != e.Length ||
                !FileArtifactDeliveryLayout.Within(e.RelativePath, RelativeDirectory)) ||
            (manifest.Directories ?? []).Any(d => !FileArtifactDeliveryLayout.Within(d, RelativeDirectory, true))) return null;
        try
        {
            return FileArtifactDeliveryLayout.Bind(manifest, manifest.Entries.Select(e =>
                new ArtifactDirectoryEntry(e.RelativePath, false, [RelativeDirectory])).Concat(
                (manifest.Directories ?? []).Select(d => new ArtifactDirectoryEntry(d, true, [RelativeDirectory]))));
        }
        catch (System.IO.InvalidDataException) { return null; }
    }
}
