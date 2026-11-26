using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Service.Components.CloudDownloads.PikPakCli;

public sealed partial class PikPakCliCloudProvider
{
    private sealed record SupersededPart(string Path, string Identity, string Version, string Replacement, string ReplacementVersion);

    private Action<string>? PlanSupersededPartCleanup(CloudDownloadRequest request, Journal journal, DownloadJournal copies)
    {
        if (copies.PreservedNativePartVersions is not {Count: > 0} versions) return null;
        var output = Path.GetFullPath(request.OutputDirectory);
        var stage = DownloadStage(output, request.OperationId);
        var manifest = new List<ArtifactManifestEntry>();
        var parts = new List<SupersededPart>();
        try
        {
            // DownloadResultService fingerprints the manifest in physical path order.
            foreach (var file in journal.Files.OrderBy(f => f.Path.Replace('/', Path.DirectorySeparatorChar), StringComparer.Ordinal))
            {
                if (!copies.Completed.TryGetValue(file.Path, out var receipt) || receipt.Size != file.Size) return null;
                manifest.Add(new(file.Path, receipt.Size, receipt.Sha256));
                if (receipt.RestoredHttp != true || !copies.Started.Contains(file.Id) || !versions.TryGetValue(file.Id, out var version) ||
                    copies.PreservedNativePartIdentities?.GetValueOrDefault(file.Id) is not {} identity ||
                    copies.RestoredHttpMembers?.GetValueOrDefault(file.Id) != RestoredHttpIdentity(request, journal, copies, file).AttemptScope)
                    continue;
                var replacement = PikPakCliStorage.Within(output, file.Path);
                using var verified = RegularFileHandle.TryOpenRead(replacement);
                if (verified != null && verified.Length == receipt.Size)
                    parts.Add(new(NativePayload(stage, file) + ".part", identity, version,
                        replacement, RegularFileHandle.ReadRetirementVersion(verified, replacement)));
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
            NotSupportedException or PikPakCliException)
        { return null; }
        if (parts.Count == 0) return null;
        var expected = ArtifactManifestFiles.Fingerprint(manifest);
        return committed =>
        {
            if (committed != expected) return;
            foreach (var part in parts)
            {
                storage?.EnsurePathAllowed(part.Path);
                PikPakCliStorage.NoLinks(part.Replacement);
                using var replacement = RegularFileHandle.TryOpenRead(part.Replacement);
                if (replacement == null || RegularFileHandle.ReadRetirementVersion(replacement, part.Replacement) != part.ReplacementVersion) continue;
                PikPakCliStorage.NoLinks(part.Path);
                if (OperatingSystem.IsWindows())
                {
                    RegularFileHandle.TryDeleteRecordedVersionOnWindows(part.Path, part.Version);
                    continue;
                }
                // Pin the leaf while the descriptor-relative unlink rechecks its parent, identity and single link.
                using var original = RegularFileHandle.TryOpenRead(part.Path);
                if (original == null || DeferredEmptyDirectoryTree.InspectPrivateFile(part.Path) is not {} observed ||
                    $"{observed.Device:X}:{observed.Inode:X}" != part.Identity ||
                    RegularFileHandle.ReadRetirementVersion(original, part.Path) != part.Version) continue;
                DeferredEmptyDirectoryTree.InspectPrivateFile(part.Path, observed);
            }
        };
    }
}
