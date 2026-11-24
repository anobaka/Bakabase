using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Service.Components.CloudDownloads;

internal static class CloudStageDirectoryPath
{
    internal static string? Download(CloudDownloadTarget target)
    {
        var entry = target.StageDirectories?.Download;
        if (entry?.BindingId == null) return null;
        if (!Guid.TryParseExact(entry.BindingId, "N", out _) || !Path.IsPathFullyQualified(entry.Directory) ||
            entry.Directory.Any(char.IsControl)) throw new InvalidOperationException("STAGE_DIRECTORY_PATH_INVALID");
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(entry.Directory));
        if (target.DownloadDirectoryScope is {} scope && !ArtifactManifestFiles.PathComparer.Equals(path,
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(scope.Directory))))
            throw new InvalidOperationException("STAGE_DIRECTORY_PATH_INVALID");
        return path;
    }

    internal static string PlannedDownload(CloudDownloadTarget target, string outputRoot, string intent) =>
        Download(target) ?? target.DownloadDirectoryScope?.Directory ?? Path.Combine(outputRoot,
            target.StoragePolicy == null ? "cloud-" + intent[..16] :
                Bakabase.Modules.Workflow.Artifacts.ArtifactStoragePolicies.RenderDownload(target.StoragePolicy,
                    target.Title, intent, target.SelectedSource!.ProviderId));

    internal static async Task<string> PrepareDownloadAsync(CloudDownloadTarget target, IUserStoragePolicy storage,
        FileProcessingWorkspaceResolver? workspaces, CancellationToken ct)
    {
        var directory = Download(target) ?? throw new InvalidOperationException("STAGE_DIRECTORY_PATH_INVALID");
        storage.EnsurePathAllowed(directory); ArtifactManifestFiles.RejectLinks(directory);
        if (target.DownloadDirectoryScope is {} scope && ArtifactManifestFiles.PathComparer.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(scope.Directory)), directory))
            return await (workspaces ?? throw new InvalidOperationException("STAGE_DIRECTORY_UNAVAILABLE"))
                .EnsureScopedDirectoryAsync(scope, ct);
        if (!Directory.Exists(directory) && !Directory.Exists(Path.GetDirectoryName(directory)))
            throw new IOException("STAGE_DIRECTORY_TARGET_UNAVAILABLE");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
