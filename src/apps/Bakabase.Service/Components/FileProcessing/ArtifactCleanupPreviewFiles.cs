using System.IO;
using System.Threading;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>Cheap observations for a confirmation preview, never proof authorizing an unlink.
/// Execution must still verify bytes, ownership and the approved selection under its fences.</summary>
internal static class ArtifactCleanupPreviewFiles
{
    internal static bool MatchesLength(string path, long expectedLength)
    {
        ArtifactManifestFiles.RejectLinks(path);
        var info = new FileInfo(path);
        info.Refresh();
        return info.Exists && !info.Attributes.HasFlag(FileAttributes.Directory) && info.Length == expectedLength;
    }

    internal static bool MatchesManifest(ArtifactRecord artifact, CancellationToken ct, bool allowConfirmedAbsent = false)
    {
        var files = ArtifactManifestFiles.ResolveFiles(artifact.RootDirectory, artifact.Manifest);
        if (!allowConfirmedAbsent) ArtifactManifestFiles.VerifyDirectories(artifact.RootDirectory, artifact.Manifest);
        else
        {
            ArtifactManifestFiles.RejectLinks(artifact.RootDirectory);
            if (!Directory.Exists(artifact.RootDirectory)) return false;
            foreach (var directory in ArtifactManifestFiles.ResolveDirectories(artifact.RootDirectory, artifact.Manifest))
            {
                var presence = ArtifactCleanupAbsence.Probe(artifact.RootDirectory, directory, ct);
                if (presence == ArtifactCleanupPathPresence.Unavailable ||
                    presence != ArtifactCleanupPathPresence.ConfirmedAbsent && !Directory.Exists(directory)) return false;
            }
        }
        for (var index = 0; index < files.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            if (allowConfirmedAbsent && ArtifactCleanupAbsence.Probe(artifact.RootDirectory, files[index], ct) ==
                ArtifactCleanupPathPresence.ConfirmedAbsent) continue;
            if (!MatchesLength(files[index], artifact.Manifest.Entries[index].Length)) return false;
        }
        return true;
    }
}
