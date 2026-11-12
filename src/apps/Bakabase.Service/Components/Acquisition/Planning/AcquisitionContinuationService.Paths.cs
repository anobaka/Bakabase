using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionContinuationService
{
    // These are filesystem fields of the supported recovery plans/worksets, not remote
    // provider paths. Nested ConfigJson uses the same inventory. Unsupported activities
    // remain rejected by the ordinary typed recovery decision at execution time.
    private static readonly HashSet<string> DirectoryFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "rootDirectory", "taskDirectory", "partDirectories", "outputDirectory", "downloadDirectory", "downloadPath",
        "processedDirectory", "publishedDirectory", "workingDirectory", "processingWorkspaceDirectory",
        "publicationWorkspaceDirectory", "workspaceDirectory", "sourceDirectory", "targetDirectory", "targetDirectories",
    };
    internal async Task<string[]> CapturePaths(AcquisitionTaskDbModel task, WorkflowRunDbModel? run,
        string settings, CancellationToken ct)
    {
        var found = new HashSet<string>(StringComparer.Ordinal); var artifactIds = new HashSet<string>(StringComparer.Ordinal);
        var downloadIds = new HashSet<int>();
        foreach (var json in new[] {task.PlanJson, run?.PlanSnapshotJson, settings})
        {
            if (string.IsNullOrWhiteSpace(json)) continue;
            using var document = JsonDocument.Parse(json); Visit(document.RootElement);
        }
        // Only this typed workset's string lists represent filesystem paths. Arbitrary
        // metadata named "files" may instead contain relative names, lengths and hashes.
        if (AcquisitionRecoveryDiagnostics.ReadWorkset(run?.CurrentItemJson) is { } workset)
        {
            AddPath(workset.OutputDirectory);
            foreach (var target in workset.Targets.Where(t => !t.Excluded))
            {
                foreach (var directory in new[] {target.DownloadDirectory, target.ProcessedDirectory,
                             target.PublishedDirectory, target.PublicationTargetDirectory}) AddPath(directory);
                foreach (var file in target.Files.Concat(target.ProcessedFiles).Concat(target.PublishedFiles)) AddPath(file);
                foreach (var id in new[] {target.DownloadArtifactId, target.ProcessedArtifactId, target.PublishedArtifactId}
                             .Concat(target.ProcessingStepArtifacts.Values))
                    if (!string.IsNullOrWhiteSpace(id)) artifactIds.Add(id);
                if (target.DownloadTaskId is { } download) downloadIds.Add(download);
                if (target.StoragePolicy != null) Visit(JsonSerializer.SerializeToElement(target.StoragePolicy, WorkflowJson.Options));
            }
        }
        if (artifactIds.Count > 1000) throw new ArgumentException("RECOVERY_SELECTION_INVALID");
        foreach (var id in artifactIds)
            if (await artifacts.GetArtifactAsync(id, ct) is { } artifact) found.Add(Path.GetFullPath(artifact.RootDirectory));
        if (downloadIds.Count > 100) throw new ArgumentException("RECOVERY_SELECTION_INVALID");
        foreach (var id in downloadIds)
        {
            var download = await db.DownloadTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
            if (download == null) continue;
            if (!string.IsNullOrWhiteSpace(download.DownloadPath)) found.Add(Path.GetFullPath(download.DownloadPath));
            if (!string.IsNullOrWhiteSpace(download.Options))
            {
                var request = JsonSerializer.Deserialize<CloudDownloadRequest>(download.Options, JsonSerializerOptions.Web);
                if (request?.Source != null && !string.IsNullOrWhiteSpace(request.OutputDirectory))
                {
                    found.Add(Path.GetFullPath(CloudDownloadDestinations.ExecutionDirectory(request)));
                    if (!string.IsNullOrWhiteSpace(request.WorkspaceDirectory)) found.Add(Path.GetFullPath(request.WorkspaceDirectory));
                }
            }
            var result = await db.DownloadResults.AsNoTracking().Where(r => r.DownloadTaskId == id).OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct);
            if (result != null && !string.IsNullOrWhiteSpace(result.DownloadDirectory)) found.Add(Path.GetFullPath(result.DownloadDirectory));
        }
        return found.Order(StringComparer.Ordinal).ToArray();

        void AddPath(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (!Path.IsPathFullyQualified(value)) throw new ArgumentException("CONTINUATION_PATH_UNSUPPORTED");
            found.Add(Path.GetFullPath(value));
        }

        void Visit(JsonElement node, bool fileValues = false, string? name = null)
        {
            ct.ThrowIfCancellationRequested();
            switch (node.ValueKind)
            {
                case JsonValueKind.Object:
                    var processingStep = node.TryGetProperty("op", out _);
                    foreach (var property in node.EnumerateObject())
                        Visit(property.Value, fileValues || DirectoryFields.Contains(property.Name) &&
                            !(processingStep && property.Name is "sourceDirectory" or "targetDirectory"), property.Name);
                    break;
                case JsonValueKind.Array:
                    foreach (var child in node.EnumerateArray()) Visit(child, fileValues, name);
                    break;
                case JsonValueKind.String:
                    var value = node.GetString()!;
                    if (fileValues && !string.IsNullOrWhiteSpace(value))
                    {
                        AddPath(value);
                    }
                    else if (name?.EndsWith("ArtifactId", StringComparison.OrdinalIgnoreCase) == true && value.Length > 0)
                        artifactIds.Add(value);
                    else if (name?.Equals("configJson", StringComparison.OrdinalIgnoreCase) == true && !string.IsNullOrWhiteSpace(value))
                    {using var config = JsonDocument.Parse(value); Visit(config.RootElement);}
                    break;
                case JsonValueKind.Number:
                    if (name?.Equals("downloadTaskId", StringComparison.OrdinalIgnoreCase) == true && node.TryGetInt32(out var downloadId) && downloadId > 0)
                        downloadIds.Add(downloadId);
                    break;
            }
        }
    }
}
