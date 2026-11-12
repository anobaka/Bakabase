using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Service.Components.CloudDownloads;

/// <summary>Downloader adapter for the source-independent operation and artifact store.</summary>
public sealed class DownloadArtifactCoordinator(BakabaseDbContext db, IArtifactOperationStore store,
    IUserStoragePolicy storage, DownloadResultService results, FileProcessingWorkspaceResolver? workspaces = null, CloudDownloadDirectoryProvenanceResolver? directoryProvenance = null)
{
    private static readonly TimeSpan ConsumerLifetime = TimeSpan.FromMinutes(5);
    public static string Consumer(WorkflowExecutionContext ctx, CloudDownloadTarget target) => $"workflow:{ctx.RunId}:{target.Id}";
    public static string Source(CloudDownloadTarget target) => CloudSourceIdentity.Key(
        target.SelectedSource ?? throw new InvalidOperationException("Choose a source."), target.AccountId);
    public static string DownloadIntent(CloudDownloadTarget target, string directory,
        FileProcessingWorkspaceResolver? workspaces = null) => CloudOperationService.Key(
        "download.intent.v1", Source(target), workspaces?.IdentityPath(Path.GetFullPath(target.DownloadDirectoryScope?.Directory ?? directory)) ??
            Path.GetFullPath(target.DownloadDirectoryScope?.Directory ?? directory),
        target.AcquisitionNonce ?? "");

    // Transport identity remains unchanged, so changing presentation never queues a new download.
    public static string ArtifactIntent(CloudDownloadTarget target, string directory,
        FileProcessingWorkspaceResolver? workspaces = null)
    {
        var intent = DownloadIntent(target, directory, workspaces);
        return target.SelectedSource?.ProviderId == "baidu" && target.DownloadLayout == "separateDirectory"
            ? CloudOperationService.Key("download.layout.v1", intent, "separateDirectory") : intent;
    }

    public async Task<CloudDownloadTarget> TryReuseAsync(WorkflowExecutionContext ctx, CloudDownloadTarget target,
        string outputDirectory, CancellationToken ct)
    {
        var intent = ArtifactIntent(target, outputDirectory, workspaces);
        var artifact = await store.ResolveAsync(intent, ct);
        if (artifact == null && target.AcquisitionNonce == null)
            artifact = await AdoptPreviousDownloadAsync(intent, target, outputDirectory, ct);
        if (artifact == null || !await store.PinConsumerAsync(artifact.OperationId, Consumer(ctx, target), "workflow", ctx.RunId.ToString(), ct))
            return target;
        foreach (var path in artifact.Files) storage.EnsurePathAllowed(path);
        await BindAsync(ctx, target, artifact.OperationId, ct);
        var metadata = ReadMetadata(artifact);
        return target with {DownloadOperationId = artifact.OperationId, DownloadArtifactId = artifact.Id,
            Files = artifact.Files.ToList(), DownloadDirectory = artifact.RootDirectory,
            DownloadTaskId = metadata?.DownloadTaskId, TransportTaskId = metadata?.TransportTaskId,
            ExecutionStatus = "reused"};
    }

    public async Task<CloudDownloadTarget> RecordAsync(WorkflowExecutionContext ctx, CloudDownloadTarget target,
        string outputDirectory, CancellationToken ct)
    {
        if (target.DownloadArtifactId != null)
        {
            if (!await store.PinConsumerAsync(target.DownloadOperationId!, Consumer(ctx, target), "workflow", ctx.RunId.ToString(), ct))
                throw new InvalidOperationException("The download is being cleaned. Retry after cleanup finishes.");
            await BindAsync(ctx, target, target.DownloadOperationId!, ct);
            return target;
        }
        var root = target.DownloadDirectory ?? throw new InvalidDataException("The download has no root.");
        CloudDownloadManifest.Validate(root, target.Files);
        foreach (var path in target.Files) storage.EnsurePathAllowed(path);
        var intent = ArtifactIntent(target, outputDirectory, workspaces);
        var metadata = new DownloadArtifactMetadata(target.DownloadTaskId, target.TransportTaskId);
        var evidence = await ExistingDirectoryEvidenceAsync(target, root, ct);
        var registration = new ArtifactRegistration(root, target.Files, "download", true,
            MetadataJson: JsonSerializer.Serialize(metadata, WorkflowJson.Options),
            CreateMetadata: evidence == null ? null : manifest =>
                JsonSerializer.Serialize(metadata with {DirectoryProvenance = evidence.Bind(manifest)}, WorkflowJson.Options));
        var artifact = await store.AdoptAsync(new(intent, intent, "download", Source(target)), registration,
            Consumer(ctx, target), ct);
        if (!await store.PinConsumerAsync(artifact.OperationId, Consumer(ctx, target), "workflow", ctx.RunId.ToString(), ct))
            throw new InvalidOperationException("The download is being cleaned. Retry after cleanup finishes.");
        await BindAsync(ctx, target, artifact.OperationId, ct);
        return target with {DownloadOperationId = artifact.OperationId, DownloadArtifactId = artifact.Id,
            ExecutionStatus = target.ExecutionStatus == "reused" ? "reused" : "completed"};
    }

    public async Task ReleaseAsync(WorkflowExecutionContext ctx, CloudDownloadTarget target, CancellationToken ct)
    {
        if (target.DownloadOperationId != null)
            await store.ReleaseConsumerAsync(target.DownloadOperationId, Consumer(ctx, target), ct);
    }

    private async Task BindAsync(WorkflowExecutionContext ctx, CloudDownloadTarget target, string operationId, CancellationToken ct)
    {
        if (!await new Bakabase.InsideWorld.Business.Components.Artifacts.ArtifactOperationBindingService(db)
                .BindAsync("workflow", ctx.RunId.ToString(), target.Id + ":artifact.download", operationId, ct))
            throw new InvalidOperationException("This run already uses a different download operation for this target.");
    }

    private async Task<ArtifactRecord?> AdoptPreviousDownloadAsync(string intent, CloudDownloadTarget target,
        string outputDirectory, CancellationToken ct)
    {
        // Compatibility is a validated read, not a data migration or a lookup by post/run identity.
        var candidates = await db.CloudDownloadOperations.AsNoTracking()
            .Where(o => o.Kind == "download" && o.DownloadTaskId != null)
            .OrderByDescending(o => o.UpdatedAt).Take(500).ToListAsync(ct);
        foreach (var operation in candidates)
        {
            CloudDownloadRequest? request;
            try { request = JsonSerializer.Deserialize<CloudDownloadRequest>(operation.RequestJson, WorkflowJson.Options); }
            catch (JsonException) { continue; }
            if (request == null || CloudSourceIdentity.Key(request.Source, request.AccountId) != Source(target)) continue;
            var relative = Path.GetRelativePath(Path.GetFullPath(outputDirectory), Path.GetFullPath(request.OutputDirectory));
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            var row = await db.DownloadTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == operation.DownloadTaskId, ct);
            if (row?.Status != DownloadTaskDbModelStatus.Complete) continue;
            var result = (await results.GetByTaskAsync(row.Id, ct)).LastOrDefault();
            if (result == null) continue;
            try
            {
                var files = JsonSerializer.Deserialize<List<string>>(result.FilesJson) ?? [];
                foreach (var file in files) storage.EnsurePathAllowed(file);
                var manifest = await Bakabase.InsideWorld.Business.Components.Artifacts.ArtifactManifestFiles.CaptureAsync(result.DownloadDirectory, files, ct);
                if (!string.Equals(manifest.Fingerprint, result.Fingerprint, StringComparison.Ordinal)) continue;
                var root = CloudDownloadPayloadRoot.SelectLayout(row.DownloadPath, result.DownloadDirectory, files, target);
                var evidence = await ExistingDirectoryEvidenceAsync(target with {DownloadTaskId = row.Id}, root, ct);
                var metadata = new DownloadArtifactMetadata(row.Id, null);
                return await store.AdoptAsync(new(intent, intent, "download", Source(target)),
                    new(root, files, "download", true, MetadataJson: JsonSerializer.Serialize(metadata, WorkflowJson.Options),
                        CreateMetadata: evidence == null ? null : captured => JsonSerializer.Serialize(
                            metadata with {DirectoryProvenance = evidence.Bind(captured)}, WorkflowJson.Options)), "legacy-download-adoption", ct);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { }
        }
        return null;
    }

    private async Task<CloudDownloadDirectoryEvidence?> ExistingDirectoryEvidenceAsync(CloudDownloadTarget target,
        string root, CancellationToken ct)
    {
        if (directoryProvenance == null || target.DownloadLayout == "separateDirectory" || target.DownloadTaskId == null) return null;
        var download = await db.DownloadTasks.AsNoTracking().SingleOrDefaultAsync(d => d.Id == target.DownloadTaskId, ct);
        if (download?.Status != DownloadTaskDbModelStatus.Complete) return null;
        var operations = await db.CloudDownloadOperations.AsNoTracking()
            .Where(o => o.Kind == "download" && o.DownloadTaskId == target.DownloadTaskId).ToListAsync(ct);
        if (operations.Count != 1) return null;
        try
        {
            var operation = operations[0];
            var request = JsonSerializer.Deserialize<CloudDownloadRequest>(operation.RequestJson, WorkflowJson.Options);
            if (request == null || request.OperationId != operation.Id ||
                CloudSourceIdentity.Key(request.Source, request.AccountId) != Source(target)) return null;
            return await directoryProvenance.InspectAsync(request, root, ct);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException)
        { return null; }
    }

    public static DownloadArtifactMetadata? ReadMetadata(ArtifactRecord artifact) => artifact.MetadataJson == null ? null :
        JsonSerializer.Deserialize<DownloadArtifactMetadata>(artifact.MetadataJson, WorkflowJson.Options);
}

public sealed record DownloadArtifactMetadata(int? DownloadTaskId, string? TransportTaskId,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    ArtifactDirectoryProvenance? DirectoryProvenance = null);
