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

    internal static bool MatchesManifest(ArtifactRecord artifact, CancellationToken ct)
    {
        var files = ArtifactManifestFiles.ResolveFiles(artifact.RootDirectory, artifact.Manifest);
        ArtifactManifestFiles.VerifyDirectories(artifact.RootDirectory, artifact.Manifest);
        for (var index = 0; index < files.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            if (!MatchesLength(files[index], artifact.Manifest.Entries[index].Length)) return false;
        }
        return true;
    }
}
