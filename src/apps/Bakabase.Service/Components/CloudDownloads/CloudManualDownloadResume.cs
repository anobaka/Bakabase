using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Exceptions;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.Bittorrent.Abstractions;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.HttpDownloads.Abstractions;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.CloudDownloads;

/// <summary>State and identity of downloads already dispatched by a particular run and target.
/// A user's selection can restart that same transfer only. It neither diagnoses its content
/// nor grants permission to change a source, request or download generation.</summary>
public sealed class CloudManualDownloadResume(BakabaseDbContext db, IBtDownloadManager? bt = null,
    IHttpDownloadManager? http = null, FileProcessingWorkspaceResolver? workspaces = null,
    IArtifactOperationStore? artifacts = null, IUserStoragePolicy? storage = null)
{
    private readonly CloudLegacyBitTorrentResume? _legacyBitTorrent =
        bt != null && artifacts != null && storage != null ? new(db, bt, http, artifacts, storage, workspaces) : null;

    internal async Task ResumeAsync(int runId, CloudDownloadTarget target, string outputRoot, DownloadTaskService sites, CancellationToken ct)
    {
        var state = await ReadAsync(runId, target, outputRoot, ct, includeLegacy: false);
        if (state.Status == "unavailable" && target.TransportTaskId?.StartsWith("bt:", StringComparison.Ordinal) == true &&
            _legacyBitTorrent != null)
        {
            await _legacyBitTorrent.ResumeAsync(runId, target, outputRoot, ct);
            return;
        }
        if (state.Status == "unavailable") throw new InvalidOperationException("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
        if (!state.CanRetry) return;
        if (target.DownloadTaskId is {} id)
        {
            // A completion or a concurrent start after preview is observed, never restarted.
            var becameActive = false;
            try
            {
                var response = await sites.StartConditionally(id, async token =>
                {
                    var latest = await ReadAsync(runId, target, outputRoot, token);
                    if (latest.CanRetry) return;
                    becameActive = latest.Status is "completed" or "running";
                    throw new InvalidOperationException("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
                }, ct);
                if (response.Code != 0) throw new InvalidOperationException(response.Message ?? "RECOVERY_DOWNLOAD_REVIEW_REQUIRED");
            }
            catch (InvalidOperationException) when (becameActive) { }
        }
        else if (state.DownloadId.StartsWith("bt:", StringComparison.Ordinal))
            await bt!.ResumeAsync(state.DownloadId[3..], ct);
        else await http!.ResumeAsync(state.DownloadId[5..], ct);
    }

    internal async Task<IReadOnlyList<CloudDownloadResumeTarget>?> ReadMultipleAsync(int runId, DownloadWorkset? workset, CancellationToken ct)
    {
        if (workset is not {Targets.Count: > 0} ||
            workset.Targets.Count == 1 && workset.NoRepeatExistingDownloads != true) return null;
        var bound = workset.Targets.Where(t => !t.Excluded && (t.DownloadTaskId.HasValue || t.TransportTaskId != null)).ToList();
        if (workset.Targets.Count == 1 && bound.Count == 0) return null;
        var rows = new List<CloudDownloadResumeTarget>(bound.Count);
        foreach (var target in bound) rows.Add(await ReadAsync(runId, target, workset.OutputDirectory, ct));
        return rows;
    }

    internal Task<CloudLegacyBitTorrentResume.Dispatch?> InspectLegacyBitTorrentAsync(int runId, CloudDownloadTarget target,
        string outputRoot, CancellationToken ct) =>
        _legacyBitTorrent?.InspectAsync(runId, target, outputRoot, ct) ?? Task.FromResult<CloudLegacyBitTorrentResume.Dispatch?>(null);

    internal async Task<CloudDownloadResumeTarget> ReadAsync(int runId, CloudDownloadTarget target, string outputRoot, CancellationToken ct,
        bool includeLegacy = true)
    {
        var downloadId = target.TransportTaskId ?? $"site:{target.DownloadTaskId}";
        CloudDownloadResumeTarget Row(string status, string? problem = null) =>
            new(target.Id, downloadId, status, problem, status is "stopped" or "failed");
        var unavailable = Row("unavailable", "RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
        if (target.Excluded || target.SelectedSource is not {} source ||
            (target.DownloadTaskId == null) == (target.TransportTaskId == null)) return unavailable;
        if (target.PublishedArtifactId != null || target.ProcessedArtifactId != null || target.DownloadArtifactId != null)
            return Row("completed");

        if (target.DownloadTaskId is {} siteId)
        {
            // Query both the original dispatch and its target binding. A shared/replaced dispatch
            // or a retargeted native task is never authorized by a stale workflow prompt.
            var ops = await db.CloudDownloadOperations.AsNoTracking().Where(o => o.DownloadTaskId == siteId)
                .Take(2).ToListAsync(ct);
            var op = ops.Count == 1 ? ops[0] : null;
            if (op == null || op.WorkflowRunId != runId || op.TargetId != target.Id || op.Kind != "download" ||
                target.DownloadOperationId != null && target.DownloadOperationId != op.Id) return unavailable;
            var slot = CloudOperationService.OperationSlot(target, "download");
            var bindings = await db.ArtifactOperationBindings.AsNoTracking().Where(b => b.OperationId == op.Id)
                .Take(2).ToListAsync(ct);
            if (bindings.Count != 1 || bindings[0].OwnerKind != "workflow" || bindings[0].OwnerId != runId.ToString() ||
                bindings[0].Slot != slot) return unavailable;
            var row = await db.DownloadTasks.AsNoTracking().SingleOrDefaultAsync(d => d.Id == siteId, ct);
            if (row == null || string.IsNullOrEmpty(row.Options))
                return unavailable;
            try
            {
                var request = JsonSerializer.Deserialize<CloudDownloadRequest>(op.RequestJson, JsonSerializerOptions.Web);
                var configured = JsonSerializer.Deserialize<CloudDownloadRequest>(row.Options, JsonSerializerOptions.Web);
                if (request == null || configured == null || row.ThirdPartyId != CloudSites.Site(source.ProviderId) || request.OperationId != op.Id ||
                    request.AccountId != (target.AccountId ?? source.AccountId) ||
                    JsonSerializer.Serialize(request, JsonSerializerOptions.Web) != JsonSerializer.Serialize(configured, JsonSerializerOptions.Web) ||
                    JsonSerializer.Serialize(request.Source, JsonSerializerOptions.Web) != JsonSerializer.Serialize(source, JsonSerializerOptions.Web) ||
                    request.RemoteEntry?.Path != target.RemoteEntry?.Path || request.RemoteEntry?.ProviderId != target.RemoteEntry?.ProviderId ||
                    request.RemoteEntry?.AccountId != target.RemoteEntry?.AccountId ||
                    !ArtifactManifestFiles.PathComparer.Equals(Path.GetFullPath(CloudDownloadDestinations.ExecutionDirectory(request)), Path.GetFullPath(row.DownloadPath)))
                    return unavailable;
            }
            catch (Exception e) when (e is JsonException or ArgumentException or IOException or NotSupportedException)
            { return unavailable; }
            return row.Status switch
            {
                DownloadTaskDbModelStatus.Disabled => Row("stopped", AcquisitionRuntimeProblems.Code(row.Message) ?? AcquisitionRuntimeProblems.Code(target.Problem)),
                DownloadTaskDbModelStatus.Failed => Row("failed", AcquisitionRuntimeProblems.Code(row.Message) ?? AcquisitionRuntimeProblems.Code(target.Problem)),
                DownloadTaskDbModelStatus.Complete => Row("completed"),
                DownloadTaskDbModelStatus.InProgress => Row("running"),
                _ => unavailable
            };
        }

        var intent = DownloadArtifactCoordinator.DownloadIntent(target, outputRoot, workspaces);
        var consumer = $"workflow:{runId}:{target.Id}";
        var liveConsumers = await db.Set<ArtifactConsumerDbModel>().AsNoTracking()
            .Where(c => c.OperationId == intent && c.ReleasedAt == null).Select(c => c.ConsumerKey).Take(2).ToListAsync(ct);
        bool Matches(string kind, string? sourceId, int? workflowRunId) =>
            kind == "workflow" && sourceId == intent && (workflowRunId == null || workflowRunId == runId) &&
            (liveConsumers.Count == 0 ? workflowRunId == runId : liveConsumers.Count == 1 && liveConsumers[0] == consumer);
        if (downloadId.StartsWith("bt:", StringComparison.Ordinal) && bt != null &&
            (await bt.ReadSnapshotAsync(ct)).FirstOrDefault(j => j.Id == downloadId[3..]) is {} torrent && Matches(torrent.SourceKind, torrent.SourceId, torrent.WorkflowRunId))
            return Row(torrent.Status.ToString().ToLowerInvariant(), AcquisitionRuntimeProblems.Code(torrent.Error));
        if (downloadId.StartsWith("http:", StringComparison.Ordinal) && http != null &&
            (await http.ReadSnapshotAsync(ct)).FirstOrDefault(j => j.Id == downloadId[5..]) is {} requestJob && Matches(requestJob.SourceKind, requestJob.SourceId, requestJob.WorkflowRunId))
            return Row(requestJob.Status.ToString().ToLowerInvariant(), AcquisitionRuntimeProblems.Code(requestJob.Error));
        try
        {
            if (includeLegacy && await InspectLegacyBitTorrentAsync(runId, target, outputRoot, ct) is { } legacy)
                return Row(legacy.Job.Status.ToString().ToLowerInvariant(), AcquisitionRuntimeProblems.Code(legacy.Job.Error));
        }
        catch (UserStoragePathException error) when (error.MountUnavailable)
        { return Row("unavailable", "LOCAL_STORAGE_UNAVAILABLE"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException)
        { return unavailable; }
        return unavailable;
    }
}
